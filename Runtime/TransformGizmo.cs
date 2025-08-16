using System;
using UnityEngine;
using System.Collections.Generic;
using CommandUndoRedo;
using RuntimeGizmos.Commands;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using System.Linq;

namespace RuntimeGizmos
{
    // To be safe, if you are changing any transforms hierarchy, such as parenting an object to something,
    // you should call ClearTargets before doing so just to be sure nothing unexpected happens... as well as call UndoRedo.Global.Clear()
    // For example, if you select an object that has children, move the children elsewhere, deselect the original object, then try to add those old children to the selection, I think it won't work.

    [RequireComponent(typeof(Camera))]
    public sealed class TransformGizmo : MonoBehaviour
    {
        public static TransformGizmo Instance { get; private set; }
        public TransformSpace space = TransformSpace.Global;
        public TransformType transformType = TransformType.Move;
        public TransformPivot pivot = TransformPivot.Pivot;
        public CenterType centerType = CenterType.All;
        public ScaleType scaleType = ScaleType.FromPoint;

        public event Action<TransformTypeChangingEventArgs> OnTransformTypeChanging;

        [Header("Input Actions")]
        [SerializeField] private InputActionReference LeftMouseButton;
        [SerializeField] private InputActionReference SelectMoveTool;
        [SerializeField] private InputActionReference SelectRotateTool;
        [SerializeField] private InputActionReference SelectScaleTool;
        [SerializeField] private InputActionReference SelectMultiTool;
        [SerializeField] private InputActionReference ToggleCenterType; // KeyCode.C
        [SerializeField] private InputActionReference TogglePivot; // KeyCode.Z
        [SerializeField] private InputActionReference ToggleScaleType; // KeyCode.S
        [SerializeField] private InputActionReference ToggleTransformSpace; // KeyCode.X
        [SerializeField] private InputActionReference SnapTranslation; // KeyCode.LeftControl
        [SerializeField] private InputActionReference AddSelection; // KeyCode.LeftShift
        [SerializeField] private InputActionReference RemoveSelection; // KeyCode.LeftControl
        [SerializeField] private InputActionReference Undo;
        [SerializeField] private InputActionReference Redo;
        [SerializeField] private InputActionReference Delete;

        [SerializeField] private float movementSnap = .25f;
        [SerializeField] private float rotationSnap = 15f;
        [SerializeField] private float scaleSnap = 1f;

        [SerializeField] private float handleLength = .2f;
        [SerializeField] private float handleWidth = .002f;
        [SerializeField] private float planeSize = .03f;
        [SerializeField] private float triangleSize = .02f;
        [SerializeField] private float boxSize = .03f;
        [SerializeField] private int circleDetail = 40;
        [SerializeField] private float allMoveHandleLengthMultiplier = 1f;
        [SerializeField] private float allRotateHandleLengthMultiplier = 1.4f;
        [SerializeField] private float allScaleHandleLengthMultiplier = 1.6f;
        [SerializeField] private float minSelectedDistanceCheck = .01f;
        [SerializeField] private float moveSpeedMultiplier = 1f;
        [SerializeField] private float scaleSpeedMultiplier = 1f;
        [SerializeField] private float rotateSpeedMultiplier = 1f;
        [SerializeField] private float allRotateSpeedMultiplier = 20f;

        [SerializeField] private bool useFirstSelectedAsMain = true;

        //If circularRotationMethod is true, when rotating you will need to move your mouse around the object as if turning a wheel.
        //If circularRotationMethod is false, when rotating you can just click and drag in a line to rotate.
        [SerializeField] private bool circularRotationMethod;
        [SerializeField] private int maxUndoStored = 100;
        [SerializeField] private LayerMask selectionMask = Physics.DefaultRaycastLayers;
        [SerializeField] private new Camera camera;

        public bool IsTransforming { get; private set; }
        public float TotalScaleAmount { get; private set; }
        public Quaternion TotalRotationAmount { get; private set; }
        public Axis TranslatingAxis => nearAxis;
        public Axis TranslatingAxisPlane => planeAxis;
        public bool HasTranslatingAxisPlane => TranslatingAxisPlane is not Axis.None and not Axis.Any;
        public TransformType TransformingType => translatingType;

        public Vector3 PivotPoint { get; private set; }
        Vector3 totalCenterPivotPoint;

        public RuntimeEditable MainTargetRoot => (targetRootsOrdered.Count > 0) ? useFirstSelectedAsMain ? targetRootsOrdered[0] : targetRootsOrdered[^1] : null;

        AxisInfo axisInfo;
        internal Axis nearAxis = Axis.None;
        Axis planeAxis = Axis.None;
        internal TransformType translatingType;

        internal readonly AxisVectors handleLines = new();
        internal readonly AxisVectors handlePlanes = new();
        internal readonly AxisVectors handleTriangles = new();
        internal readonly AxisVectors handleSquares = new();
        internal readonly AxisVectors circlesLines = new();

        //We use a HashSet and a List for targetRoots so that we get fast lookup with the hashset while also keeping track of the order with the list.
        readonly List<RuntimeEditable> targetRootsOrdered = new();
        readonly Dictionary<RuntimeEditable, TargetInfo> targetRoots = new();
        public readonly HashSet<Renderer> highlightedRenderers = new();
        readonly HashSet<Transform> children = new();

        readonly List<Transform> childrenTransformBuffer = new();
        readonly List<Renderer> renderersBuffer = new();
        readonly List<Material> materialsBuffer = new();

        void OnEnable()
        {
            if (Instance != null)
            {
                Debug.LogError("Found two active TransformGizmo components");
                enabled = false;
                return;
            }
            Instance = this;

            UndoRedo.Global.MaxUndoStored = maxUndoStored;

            RegisterNullSafe(LeftMouseButton, MouseClicked);

            RegisterNullSafe(SelectMoveTool, SelectMoveToolCmd);
            RegisterNullSafe(SelectRotateTool, SelectRotateToolCmd);
            RegisterNullSafe(SelectScaleTool, SelectScaleToolCmd);
            RegisterNullSafe(SelectMultiTool, SelectMultiToolCmd);
            RegisterNullSafe(TogglePivot, TogglePivotCmd);
            RegisterNullSafe(ToggleCenterType, ToggleCenterTypeCmd);
            RegisterNullSafe(ToggleScaleType, ToggleScaleTypeCmd);
            RegisterNullSafe(ToggleTransformSpace, ToggleSpaceCmd);
            RegisterNullSafe(Undo, UndoCmd);
            RegisterNullSafe(Redo, RedoCmd);
            RegisterNullSafe(Delete, DeleteCmd);
        }

        void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            ClearTargets();

            UnregisterNullSafe(LeftMouseButton, MouseClicked);

            UnregisterNullSafe(SelectMoveTool, SelectMoveToolCmd);
            UnregisterNullSafe(SelectRotateTool, SelectRotateToolCmd);
            UnregisterNullSafe(SelectScaleTool, SelectScaleToolCmd);
            UnregisterNullSafe(SelectMultiTool, SelectMultiToolCmd);
            UnregisterNullSafe(TogglePivot, TogglePivotCmd);
            UnregisterNullSafe(ToggleCenterType, ToggleCenterTypeCmd);
            UnregisterNullSafe(ToggleScaleType, ToggleScaleTypeCmd);
            UnregisterNullSafe(ToggleTransformSpace, ToggleSpaceCmd);
            UnregisterNullSafe(Undo, UndoCmd);
            UnregisterNullSafe(Redo, RedoCmd);
            UnregisterNullSafe(Delete, DeleteCmd);
        }

        void OnDestroy()
        {
            ClearAllHighlightedRenderers();
        }

        void Update()
        {
            if (!IsTransforming) translatingType = transformType;

            SetNearAxis();
        }

        void LateUpdate()
        {
            if (MainTargetRoot == null) return;

            // We run this in lateupdate since coroutines run after update and we want our gizmos to have the updated target transform position after TransformSelected()
            SetAxisInfo();

            SetLines();
        }

        // We only support scaling in local space.
        public TransformSpace GetProperTransformSpace() => transformType == TransformType.Scale ? TransformSpace.Local : space;

        public bool TransformTypeContains(TransformType type) => TransformTypeContains(transformType, type);
        public bool TranslatingTypeContains(TransformType type, bool checkIsTransforming = true)
        {
            TransformType transType = !checkIsTransforming || IsTransforming ? translatingType : transformType;
            return TransformTypeContains(transType, type);
        }
        public bool TransformTypeContains(TransformType mainType, TransformType type) => ExtTransformType.TransformTypeContains(mainType, type, GetProperTransformSpace());

        public float GetHandleLength(TransformType type, Axis axis = Axis.None, bool multiplyDistanceMultiplier = true)
        {
            float length = handleLength;
            if (transformType is TransformType.All)
            {
                if (type is TransformType.Move) length *= allMoveHandleLengthMultiplier;
                if (type is TransformType.Rotate) length *= allRotateHandleLengthMultiplier;
                if (type is TransformType.Scale) length *= allScaleHandleLengthMultiplier;
            }

            if (multiplyDistanceMultiplier) length *= GetDistanceMultiplier();

            if (type is TransformType.Scale && IsTransforming && (TranslatingAxis == axis || TranslatingAxis == Axis.Any)) length += TotalScaleAmount;

            return length;
        }

        private readonly List<RaycastResult> uiRaycastResults = new();
        void MouseClicked(InputAction.CallbackContext context)
        {
            if (Cursor.lockState is CursorLockMode.Locked) return;
            if (EventSystem.current != null)
            {
                EventSystem.current.RaycastAll(new(EventSystem.current) { position = Pointer.current.position.value }, uiRaycastResults);
                var ishit = uiRaycastResults.Any(r => r.isValid);
                uiRaycastResults.Clear();
                if (ishit) return;
            }

            if (nearAxis is Axis.None)
            {
                GetTarget(context);
            }

            if (MainTargetRoot == null || nearAxis is Axis.None)
            {
                return;
            }

            _ = TransformSelected(translatingType);
        }

        private async Awaitable TransformSelected(TransformType transType)
        {
            IsTransforming = true;
            TotalScaleAmount = 0;
            TotalRotationAmount = Quaternion.identity;

            Vector3 originalPivot = PivotPoint;

            Vector3 axis = GetNearAxisDirection(out Vector3 otherAxis1, out Vector3 otherAxis2);
            Vector3 planeNormal = HasTranslatingAxisPlane ? axis : (transform.position - originalPivot).normalized;
            Vector3 projectedAxis = Vector3.ProjectOnPlane(axis, planeNormal).normalized;
            Vector3 previousMousePosition = Vector3.zero;

            Vector3 currentSnapMovementAmount = Vector3.zero;
            float currentSnapRotationAmount = 0;
            float currentSnapScaleAmount = 0;

            List<ICommand> transformCommands = new();
            for (int i = 0; i < targetRootsOrdered.Count; i++)
            {
                transformCommands.Add(new TransformCommand(this, targetRootsOrdered[i].transform));
            }

            while (LeftMouseButton.action.IsPressed())
            {
                Ray mouseRay = camera.ScreenPointToRay(Mouse.current.position.value);
                Vector3 mousePosition = Geometry.LinePlaneIntersect(mouseRay.origin, mouseRay.direction, originalPivot, planeNormal);
                bool isSnapping = SnapTranslation != null && SnapTranslation.action.IsPressed();

                if (previousMousePosition != Vector3.zero && mousePosition != Vector3.zero)
                {
                    if (transType == TransformType.Move)
                    {
                        Vector3 movement;

                        if (HasTranslatingAxisPlane)
                        {
                            movement = mousePosition - previousMousePosition;
                        }
                        else
                        {
                            float moveAmount = ExtVector3.MagnitudeInDirection(mousePosition - previousMousePosition, projectedAxis) * moveSpeedMultiplier;
                            movement = axis * moveAmount;
                        }

                        if (isSnapping && movementSnap > 0)
                        {
                            currentSnapMovementAmount += movement;
                            movement = Vector3.zero;

                            if (HasTranslatingAxisPlane)
                            {
                                float amountInAxis1 = ExtVector3.MagnitudeInDirection(currentSnapMovementAmount, otherAxis1);
                                float amountInAxis2 = ExtVector3.MagnitudeInDirection(currentSnapMovementAmount, otherAxis2);
                                float snapAmount1 = CalculateSnapAmount(movementSnap, amountInAxis1, out _);
                                float snapAmount2 = CalculateSnapAmount(movementSnap, amountInAxis2, out _);

                                if (snapAmount1 != 0)
                                {
                                    Vector3 snapMove = otherAxis1 * snapAmount1;
                                    movement += snapMove;
                                    currentSnapMovementAmount -= snapMove;
                                }
                                if (snapAmount2 != 0)
                                {
                                    Vector3 snapMove = otherAxis2 * snapAmount2;
                                    movement += snapMove;
                                    currentSnapMovementAmount -= snapMove;
                                }
                            }
                            else
                            {
                                float snapAmount = CalculateSnapAmount(movementSnap, currentSnapMovementAmount.magnitude, out float remainder);

                                if (snapAmount != 0)
                                {
                                    movement = currentSnapMovementAmount.normalized * snapAmount;
                                    currentSnapMovementAmount = currentSnapMovementAmount.normalized * remainder;
                                }
                            }
                        }

                        for (int i = 0; i < targetRootsOrdered.Count; i++)
                        {
                            targetRootsOrdered[i].transform.Translate(movement, Space.World);
                        }

                        SetPivotPointOffset(movement);
                    }
                    else if (transType == TransformType.Scale)
                    {
                        Vector3 projected = (nearAxis == Axis.Any) ? transform.right : projectedAxis;
                        float scaleAmount = ExtVector3.MagnitudeInDirection(mousePosition - previousMousePosition, projected) * scaleSpeedMultiplier;

                        if (isSnapping && scaleSnap > 0)
                        {
                            currentSnapScaleAmount += scaleAmount;
                            scaleAmount = 0;

                            float snapAmount = CalculateSnapAmount(scaleSnap, currentSnapScaleAmount, out var remainder);

                            if (snapAmount != 0)
                            {
                                scaleAmount = snapAmount;
                                currentSnapScaleAmount = remainder;
                            }
                        }

                        Vector3 localAxis = (GetProperTransformSpace() == TransformSpace.Local && nearAxis != Axis.Any) ? MainTargetRoot.transform.InverseTransformDirection(axis) : axis;
                        Vector3 targetScaleAmount;
                        if (nearAxis == Axis.Any) targetScaleAmount = ExtVector3.Abs(MainTargetRoot.transform.localScale.normalized) * scaleAmount;
                        else targetScaleAmount = localAxis * scaleAmount;

                        for (int i = 0; i < targetRootsOrdered.Count; i++)
                        {
                            var target = targetRootsOrdered[i].transform;

                            var targetScale = target.localScale + targetScaleAmount;

                            if (pivot == TransformPivot.Pivot)
                            {
                                target.localScale = targetScale;
                            }
                            else if (pivot == TransformPivot.Center)
                            {
                                if (scaleType == ScaleType.FromPoint)
                                {
                                    target.SetScaleFrom(originalPivot, targetScale);
                                }
                                else if (scaleType == ScaleType.FromPointOffset)
                                {
                                    target.SetScaleFromOffset(originalPivot, targetScale);
                                }
                            }
                        }

                        TotalScaleAmount += scaleAmount;
                    }
                    else if (transType == TransformType.Rotate)
                    {
                        Vector3 rotationAxis = axis;

                        float rotateAmount;
                        if (nearAxis == Axis.Any)
                        {
                            Vector3 rotation = transform.TransformDirection(new Vector3(Mouse.current.delta.y.value, -Mouse.current.delta.x.value, 0));
                            Quaternion.Euler(rotation).ToAngleAxis(out rotateAmount, out rotationAxis);
                            rotateAmount *= allRotateSpeedMultiplier;
                        }
                        else
                        {
                            if (circularRotationMethod)
                            {
                                float angle = Vector3.SignedAngle(previousMousePosition - originalPivot, mousePosition - originalPivot, axis);
                                rotateAmount = angle * rotateSpeedMultiplier;
                            }
                            else
                            {
                                Vector3 projected = (nearAxis == Axis.Any || ExtVector3.IsParallel(axis, planeNormal)) ? planeNormal : Vector3.Cross(axis, planeNormal);
                                rotateAmount = ExtVector3.MagnitudeInDirection(mousePosition - previousMousePosition, projected) * (rotateSpeedMultiplier * 100f) / GetDistanceMultiplier();
                            }
                        }

                        if (isSnapping && rotationSnap > 0)
                        {
                            currentSnapRotationAmount += rotateAmount;
                            rotateAmount = 0;

                            float snapAmount = CalculateSnapAmount(rotationSnap, currentSnapRotationAmount, out float remainder);

                            if (snapAmount != 0)
                            {
                                rotateAmount = snapAmount;
                                currentSnapRotationAmount = remainder;
                            }
                        }

                        for (int i = 0; i < targetRootsOrdered.Count; i++)
                        {
                            var target = targetRootsOrdered[i].transform;

                            if (pivot is TransformPivot.Pivot)
                            {
                                target.Rotate(rotationAxis, rotateAmount, Space.World);
                            }
                            else if (pivot is TransformPivot.Center)
                            {
                                target.RotateAround(originalPivot, rotationAxis, rotateAmount);
                            }
                        }

                        TotalRotationAmount *= Quaternion.Euler(rotationAxis * rotateAmount);
                    }
                }

                previousMousePosition = mousePosition;

                await Awaitable.EndOfFrameAsync();
            }

            foreach (var cmd in transformCommands.Cast<TransformCommand>())
            {
                cmd.StoreNewTransformValues();
            }
            CommandGroup commandGroup = new();
            commandGroup.Set(transformCommands);
            UndoRedo.Global.Insert(commandGroup);

            TotalRotationAmount = Quaternion.identity;
            TotalScaleAmount = 0;
            IsTransforming = false;
            SetTranslatingAxis(transformType, Axis.None);

            SetPivotPoint();
        }

        private float CalculateSnapAmount(float snapValue, float currentAmount, out float remainder)
        {
            remainder = 0;
            if (snapValue <= 0) return currentAmount;

            float currentAmountAbs = Mathf.Abs(currentAmount);
            if (currentAmountAbs > snapValue)
            {
                remainder = currentAmountAbs % snapValue;
                return snapValue * (Mathf.Sign(currentAmount) * Mathf.Floor(currentAmountAbs / snapValue));
            }

            return 0;
        }

        private Vector3 GetNearAxisDirection(out Vector3 otherAxis1, out Vector3 otherAxis2)
        {
            otherAxis1 = otherAxis2 = Vector3.zero;

            if (nearAxis != Axis.None)
            {
                if (nearAxis == Axis.X)
                {
                    otherAxis1 = axisInfo.yDirection;
                    otherAxis2 = axisInfo.zDirection;
                    return axisInfo.xDirection;
                }
                if (nearAxis == Axis.Y)
                {
                    otherAxis1 = axisInfo.xDirection;
                    otherAxis2 = axisInfo.zDirection;
                    return axisInfo.yDirection;
                }
                if (nearAxis == Axis.Z)
                {
                    otherAxis1 = axisInfo.xDirection;
                    otherAxis2 = axisInfo.yDirection;
                    return axisInfo.zDirection;
                }
                if (nearAxis == Axis.Any)
                {
                    return Vector3.one;
                }
            }

            return Vector3.zero;
        }

        private void GetTarget(InputAction.CallbackContext context)
        {
            bool isAdding = AddSelection != null && AddSelection.action.IsPressed();
            bool isRemoving = RemoveSelection != null && RemoveSelection.action.IsPressed();

            if (Physics.Raycast(camera.ScreenPointToRay(Mouse.current.position.value), out RaycastHit hitInfo, Mathf.Infinity, selectionMask))
            {
                var obj = hitInfo.collider.GetComponentInParent<RuntimeEditable>();
                if (obj != null)
                {
                    if (isAdding)
                    {
                        AddTarget(obj);
                    }
                    else if (isRemoving)
                    {
                        RemoveTarget(obj);
                    }
                    else if (!isAdding && !isRemoving)
                    {
                        ClearAndAddTarget(obj);
                    }

                    return;
                }
            }

            if (!isAdding && !isRemoving)
            {
                ClearTargets();
            }
        }

        public void AddTarget(RuntimeEditable target, bool addCommand = true)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (targetRoots.ContainsKey(target)) return;
            if (children.Contains(target.transform)) return;

            if (addCommand) UndoRedo.Global.Insert(new AddTargetCommand(this, target, targetRootsOrdered));

            AddTargetRoot(target);
            AddTargetHighlightedRenderers(target);

            SetPivotPoint();
        }

        public void RemoveTarget(RuntimeEditable target, bool addCommand = true)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }
            if (!targetRoots.ContainsKey(target)) return;

            if (addCommand) UndoRedo.Global.Insert(new RemoveTargetCommand(this, target));

            RemoveTargetHighlightedRenderers(target);
            RemoveTargetRoot(target);

            SetPivotPoint();
        }

        public void ClearTargets(bool addCommand = true)
        {
            if (addCommand) UndoRedo.Global.Insert(new ClearTargetsCommand(this, targetRootsOrdered));

            ClearAllHighlightedRenderers();
            targetRoots.Clear();
            targetRootsOrdered.Clear();
            children.Clear();
        }

        private void ClearAndAddTarget(RuntimeEditable target)
        {
            UndoRedo.Global.Insert(new ClearAndAddTargetCommand(this, target, targetRootsOrdered));

            ClearTargets(false);
            AddTarget(target, false);
        }

        private void AddTargetHighlightedRenderers(RuntimeEditable target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }
            GetTargetRenderers(target, renderersBuffer);

            for (int i = 0; i < renderersBuffer.Count; i++)
            {
                Renderer render = renderersBuffer[i];

                if (!highlightedRenderers.Contains(render))
                {
                    highlightedRenderers.Add(render);
                }
            }

            materialsBuffer.Clear();
        }

        private void GetTargetRenderers(RuntimeEditable target, List<Renderer> renderers)
        {
            if (target == null) return;
            renderers.Clear();
            target.GetComponentsInChildren(true, renderers);
        }

        private void ClearAllHighlightedRenderers()
        {
            foreach (var target in targetRoots)
            {
                RemoveTargetHighlightedRenderers(target.Key);
            }

            // In case any are still left, such as if they changed parents or what not when they were highlighted.
            renderersBuffer.Clear();
            renderersBuffer.AddRange(highlightedRenderers);
            RemoveHighlightedRenderers(renderersBuffer);
        }

        private void RemoveTargetHighlightedRenderers(RuntimeEditable target)
        {
            GetTargetRenderers(target, renderersBuffer);

            RemoveHighlightedRenderers(renderersBuffer);
        }

        private void RemoveHighlightedRenderers(List<Renderer> renderers)
        {
            for (int i = 0; i < renderers.Count; i++)
            {
                highlightedRenderers.Remove(renderers[i]);
            }

            renderers.Clear();
        }

        private void AddTargetRoot(RuntimeEditable targetRoot)
        {
            targetRoots.Add(targetRoot, new TargetInfo());
            targetRootsOrdered.Add(targetRoot);

            AddAllChildren(targetRoot);
        }
        private void RemoveTargetRoot(RuntimeEditable targetRoot)
        {
            if (targetRoots.Remove(targetRoot))
            {
                targetRootsOrdered.Remove(targetRoot);

                RemoveAllChildren(targetRoot);
            }
        }

        private void AddAllChildren(RuntimeEditable target)
        {
            childrenTransformBuffer.Clear();
            target.GetComponentsInChildren(true, childrenTransformBuffer);
            childrenTransformBuffer.Remove(target.transform);

            for (int i = 0; i < childrenTransformBuffer.Count; i++)
            {
                Transform child = childrenTransformBuffer[i];
                children.Add(child);
                if (child.TryGetComponent<RuntimeEditable>(out var c))
                {
                    RemoveTargetRoot(c);
                }
            }

            childrenTransformBuffer.Clear();
        }
        void RemoveAllChildren(RuntimeEditable target)
        {
            childrenTransformBuffer.Clear();
            target.GetComponentsInChildren(true, childrenTransformBuffer);
            childrenTransformBuffer.Remove(target.transform);

            for (int i = 0; i < childrenTransformBuffer.Count; i++)
            {
                children.Remove(childrenTransformBuffer[i]);
            }

            childrenTransformBuffer.Clear();
        }

        public void SetPivotPoint()
        {
            if (MainTargetRoot == null)
            {
                return;
            }

            if (pivot is TransformPivot.Pivot)
            {
                PivotPoint = MainTargetRoot.transform.position;
            }
            else if (pivot is TransformPivot.Center)
            {
                totalCenterPivotPoint = Vector3.zero;

                var targetsEnumerator = targetRoots.GetEnumerator();
                while (targetsEnumerator.MoveNext())
                {
                    var target = targetsEnumerator.Current.Key;
                    TargetInfo info = targetsEnumerator.Current.Value;
                    info.centerPivotPoint = target.transform.GetCenter(centerType);

                    totalCenterPivotPoint += info.centerPivotPoint;
                }

                totalCenterPivotPoint /= targetRoots.Count;

                if (centerType == CenterType.Solo)
                {
                    PivotPoint = targetRoots[MainTargetRoot].centerPivotPoint;
                }
                else if (centerType == CenterType.All)
                {
                    PivotPoint = totalCenterPivotPoint;
                }
            }
        }
        void SetPivotPointOffset(Vector3 offset)
        {
            PivotPoint += offset;
            totalCenterPivotPoint += offset;
        }

        public void SetTranslatingAxis(TransformType type, Axis axis, Axis planeAxis = Axis.None)
        {
            this.translatingType = type;
            this.nearAxis = axis;
            this.planeAxis = planeAxis;
        }

        public AxisInfo GetAxisInfo()
        {
            AxisInfo currentAxisInfo = axisInfo;

            if (IsTransforming && GetProperTransformSpace() == TransformSpace.Global && translatingType == TransformType.Rotate)
            {
                currentAxisInfo.xDirection = TotalRotationAmount * Vector3.right;
                currentAxisInfo.yDirection = TotalRotationAmount * Vector3.up;
                currentAxisInfo.zDirection = TotalRotationAmount * Vector3.forward;
            }

            return currentAxisInfo;
        }

        private void SetNearAxis()
        {
            if (IsTransforming) return;

            SetTranslatingAxis(transformType, Axis.None);

            if (MainTargetRoot == null || Cursor.lockState is CursorLockMode.Locked) return;

            float distanceMultiplier = GetDistanceMultiplier();
            float handleMinSelectedDistanceCheck = (this.minSelectedDistanceCheck + handleWidth) * distanceMultiplier;

            if (nearAxis is Axis.None && (TransformTypeContains(TransformType.Move) || TransformTypeContains(TransformType.Scale)))
            {
                //Important to check scale lines before move lines since in TransformType.All the move planes would block the scales center scale all gizmo.
                if (nearAxis is Axis.None && TransformTypeContains(TransformType.Scale))
                {
                    float tipMinSelectedDistanceCheck = (this.minSelectedDistanceCheck + boxSize) * distanceMultiplier;
                    HandleNearestPlanes(TransformType.Scale, handleSquares, tipMinSelectedDistanceCheck);
                }

                if (nearAxis is Axis.None && TransformTypeContains(TransformType.Move))
                {
                    //Important to check the planes first before the handle tip since it makes selecting the planes easier.
                    float planeMinSelectedDistanceCheck = (this.minSelectedDistanceCheck + planeSize) * distanceMultiplier;
                    HandleNearestPlanes(TransformType.Move, handlePlanes, planeMinSelectedDistanceCheck);

                    if (nearAxis is not Axis.None)
                    {
                        planeAxis = nearAxis;
                    }
                    else
                    {
                        float tipMinSelectedDistanceCheck = (this.minSelectedDistanceCheck + triangleSize) * distanceMultiplier;
                        HandleNearestLines(TransformType.Move, handleTriangles, tipMinSelectedDistanceCheck);
                    }
                }

                if (nearAxis is Axis.None)
                {
                    //Since Move and Scale share the same handle line, we give Move the priority.
                    TransformType transType = transformType is TransformType.All ? TransformType.Move : transformType;
                    HandleNearestLines(transType, handleLines, handleMinSelectedDistanceCheck);
                }
            }

            if (nearAxis is Axis.None && TransformTypeContains(TransformType.Rotate))
            {
                HandleNearestLines(TransformType.Rotate, circlesLines, handleMinSelectedDistanceCheck);
            }
        }

        void HandleNearestLines(TransformType type, AxisVectors axisVectors, float minSelectedDistanceCheck)
        {
            float xClosestDistance = ClosestDistanceFromMouseToLines(axisVectors.x);
            float yClosestDistance = ClosestDistanceFromMouseToLines(axisVectors.y);
            float zClosestDistance = ClosestDistanceFromMouseToLines(axisVectors.z);
            float allClosestDistance = ClosestDistanceFromMouseToLines(axisVectors.all);

            HandleNearest(type, xClosestDistance, yClosestDistance, zClosestDistance, allClosestDistance, minSelectedDistanceCheck);
        }

        void HandleNearestPlanes(TransformType type, AxisVectors axisVectors, float minSelectedDistanceCheck)
        {
            float xClosestDistance = ClosestDistanceFromMouseToPlanes(axisVectors.x);
            float yClosestDistance = ClosestDistanceFromMouseToPlanes(axisVectors.y);
            float zClosestDistance = ClosestDistanceFromMouseToPlanes(axisVectors.z);
            float allClosestDistance = ClosestDistanceFromMouseToPlanes(axisVectors.all);

            HandleNearest(type, xClosestDistance, yClosestDistance, zClosestDistance, allClosestDistance, minSelectedDistanceCheck);
        }

        void HandleNearest(TransformType type, float xClosestDistance, float yClosestDistance, float zClosestDistance, float allClosestDistance, float minSelectedDistanceCheck)
        {
            if (type == TransformType.Scale && allClosestDistance <= minSelectedDistanceCheck) SetTranslatingAxis(type, Axis.Any);
            else if (xClosestDistance <= minSelectedDistanceCheck && xClosestDistance <= yClosestDistance && xClosestDistance <= zClosestDistance) SetTranslatingAxis(type, Axis.X);
            else if (yClosestDistance <= minSelectedDistanceCheck && yClosestDistance <= xClosestDistance && yClosestDistance <= zClosestDistance) SetTranslatingAxis(type, Axis.Y);
            else if (zClosestDistance <= minSelectedDistanceCheck && zClosestDistance <= xClosestDistance && zClosestDistance <= yClosestDistance) SetTranslatingAxis(type, Axis.Z);
            else if (type == TransformType.Rotate && MainTargetRoot != null)
            {
                Ray mouseRay = camera.ScreenPointToRay(Mouse.current.position.value);
                Vector3 mousePlaneHit = Geometry.LinePlaneIntersect(mouseRay.origin, mouseRay.direction, PivotPoint, (transform.position - PivotPoint).normalized);
                if ((PivotPoint - mousePlaneHit).sqrMagnitude <= GetHandleLength(TransformType.Rotate).Squared()) SetTranslatingAxis(type, Axis.Any);
            }
        }

        float ClosestDistanceFromMouseToLines(List<Vector3> lines)
        {
            Ray mouseRay = camera.ScreenPointToRay(Mouse.current.position.value);

            float closestDistance = float.MaxValue;
            for (int i = 0; i + 1 < lines.Count; i++)
            {
                IntersectPoints points = Geometry.ClosestPointsOnSegmentToLine(lines[i], lines[i + 1], mouseRay.origin, mouseRay.direction);
                float distance = Vector3.Distance(points.first, points.second);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                }
            }
            return closestDistance;
        }

        float ClosestDistanceFromMouseToPlanes(List<Vector3> planePoints)
        {
            float closestDistance = float.MaxValue;

            if (planePoints.Count >= 4)
            {
                Ray mouseRay = camera.ScreenPointToRay(Mouse.current.position.value);

                for (int i = 0; i < planePoints.Count; i += 4)
                {
                    Plane plane = new(planePoints[i], planePoints[i + 1], planePoints[i + 2]);

                    if (plane.Raycast(mouseRay, out float distanceToPlane))
                    {
                        Vector3 pointOnPlane = mouseRay.origin + (mouseRay.direction * distanceToPlane);
                        Vector3 planeCenter = (planePoints[0] + planePoints[1] + planePoints[2] + planePoints[3]) / 4f;

                        float distance = Vector3.Distance(planeCenter, pointOnPlane);
                        if (distance < closestDistance)
                        {
                            closestDistance = distance;
                        }
                    }
                }
            }

            return closestDistance;
        }

        void SetAxisInfo()
        {
            axisInfo.Set(MainTargetRoot.transform, PivotPoint, GetProperTransformSpace());
        }

        // This helps keep the size consistent no matter how far we are from it.
        public float GetDistanceMultiplier()
        {
            if (MainTargetRoot == null) return 0f;

            if (camera.orthographic) return Mathf.Max(.01f, camera.orthographicSize * 2f);
            return Mathf.Max(.01f, Mathf.Abs(ExtVector3.MagnitudeInDirection(PivotPoint - transform.position, camera.transform.forward)));
        }

        void SetLines()
        {
            SetHandleLines();
            SetHandlePlanes();
            SetHandleTriangles();
            SetHandleSquares();
            SetCircles(GetAxisInfo(), circlesLines);
        }

        void SetHandleLines()
        {
            handleLines.Clear();

            if (TranslatingTypeContains(TransformType.Move) || TranslatingTypeContains(TransformType.Scale))
            {
                float lineWidth = handleWidth * GetDistanceMultiplier();

                float xLineLength = 0;
                float yLineLength = 0;
                float zLineLength = 0;
                if (TranslatingTypeContains(TransformType.Move))
                {
                    xLineLength = yLineLength = zLineLength = GetHandleLength(TransformType.Move);
                }
                else if (TranslatingTypeContains(TransformType.Scale))
                {
                    xLineLength = GetHandleLength(TransformType.Scale, Axis.X);
                    yLineLength = GetHandleLength(TransformType.Scale, Axis.Y);
                    zLineLength = GetHandleLength(TransformType.Scale, Axis.Z);
                }

                AddQuads(PivotPoint, axisInfo.xDirection, axisInfo.yDirection, axisInfo.zDirection, xLineLength, lineWidth, handleLines.x);
                AddQuads(PivotPoint, axisInfo.yDirection, axisInfo.xDirection, axisInfo.zDirection, yLineLength, lineWidth, handleLines.y);
                AddQuads(PivotPoint, axisInfo.zDirection, axisInfo.xDirection, axisInfo.yDirection, zLineLength, lineWidth, handleLines.z);
            }
        }

        void SetHandlePlanes()
        {
            handlePlanes.Clear();

            if (TranslatingTypeContains(TransformType.Move))
            {
                Vector3 pivotToCamera = camera.transform.position - PivotPoint;
                float cameraXSign = Mathf.Sign(Vector3.Dot(axisInfo.xDirection, pivotToCamera));
                float cameraYSign = Mathf.Sign(Vector3.Dot(axisInfo.yDirection, pivotToCamera));
                float cameraZSign = Mathf.Sign(Vector3.Dot(axisInfo.zDirection, pivotToCamera));

                float planeSize = this.planeSize;
                if (transformType == TransformType.All) { planeSize *= allMoveHandleLengthMultiplier; }
                planeSize *= GetDistanceMultiplier();

                Vector3 xDirection = cameraXSign * planeSize * axisInfo.xDirection;
                Vector3 yDirection = cameraYSign * planeSize * axisInfo.yDirection;
                Vector3 zDirection = cameraZSign * planeSize * axisInfo.zDirection;

                Vector3 xPlaneCenter = PivotPoint + (yDirection + zDirection);
                Vector3 yPlaneCenter = PivotPoint + (xDirection + zDirection);
                Vector3 zPlaneCenter = PivotPoint + (xDirection + yDirection);

                AddQuad(xPlaneCenter, axisInfo.yDirection, axisInfo.zDirection, planeSize, handlePlanes.x);
                AddQuad(yPlaneCenter, axisInfo.xDirection, axisInfo.zDirection, planeSize, handlePlanes.y);
                AddQuad(zPlaneCenter, axisInfo.xDirection, axisInfo.yDirection, planeSize, handlePlanes.z);
            }
        }

        void SetHandleTriangles()
        {
            handleTriangles.Clear();

            if (TranslatingTypeContains(TransformType.Move))
            {
                float triangleLength = triangleSize * GetDistanceMultiplier();
                AddTriangles(axisInfo.GetXAxisEnd(GetHandleLength(TransformType.Move)), axisInfo.xDirection, axisInfo.yDirection, axisInfo.zDirection, triangleLength, handleTriangles.x);
                AddTriangles(axisInfo.GetYAxisEnd(GetHandleLength(TransformType.Move)), axisInfo.yDirection, axisInfo.xDirection, axisInfo.zDirection, triangleLength, handleTriangles.y);
                AddTriangles(axisInfo.GetZAxisEnd(GetHandleLength(TransformType.Move)), axisInfo.zDirection, axisInfo.yDirection, axisInfo.xDirection, triangleLength, handleTriangles.z);
            }
        }

        void AddTriangles(Vector3 axisEnd, Vector3 axisDirection, Vector3 axisOtherDirection1, Vector3 axisOtherDirection2, float size, List<Vector3> resultsBuffer)
        {
            Vector3 endPoint = axisEnd + (axisDirection * (size * 2f));
            Square baseSquare = GetBaseSquare(axisEnd, axisOtherDirection1, axisOtherDirection2, size / 2f);

            resultsBuffer.Add(baseSquare.bottomLeft);
            resultsBuffer.Add(baseSquare.topLeft);
            resultsBuffer.Add(baseSquare.topRight);
            resultsBuffer.Add(baseSquare.topLeft);
            resultsBuffer.Add(baseSquare.bottomRight);
            resultsBuffer.Add(baseSquare.topRight);

            for (int i = 0; i < 4; i++)
            {
                resultsBuffer.Add(baseSquare[i]);
                resultsBuffer.Add(baseSquare[i + 1]);
                resultsBuffer.Add(endPoint);
            }
        }

        void SetHandleSquares()
        {
            handleSquares.Clear();

            if (TranslatingTypeContains(TransformType.Scale))
            {
                float boxSize = this.boxSize * GetDistanceMultiplier();
                AddSquares(axisInfo.GetXAxisEnd(GetHandleLength(TransformType.Scale, Axis.X)), axisInfo.xDirection, axisInfo.yDirection, axisInfo.zDirection, boxSize, handleSquares.x);
                AddSquares(axisInfo.GetYAxisEnd(GetHandleLength(TransformType.Scale, Axis.Y)), axisInfo.yDirection, axisInfo.xDirection, axisInfo.zDirection, boxSize, handleSquares.y);
                AddSquares(axisInfo.GetZAxisEnd(GetHandleLength(TransformType.Scale, Axis.Z)), axisInfo.zDirection, axisInfo.xDirection, axisInfo.yDirection, boxSize, handleSquares.z);
                AddSquares(PivotPoint - (axisInfo.xDirection * (boxSize * .5f)), axisInfo.xDirection, axisInfo.yDirection, axisInfo.zDirection, boxSize, handleSquares.all);
            }
        }

        void AddSquares(Vector3 axisStart, Vector3 axisDirection, Vector3 axisOtherDirection1, Vector3 axisOtherDirection2, float size, List<Vector3> resultsBuffer)
        {
            AddQuads(axisStart, axisDirection, axisOtherDirection1, axisOtherDirection2, size, size * .5f, resultsBuffer);
        }
        void AddQuads(Vector3 axisStart, Vector3 axisDirection, Vector3 axisOtherDirection1, Vector3 axisOtherDirection2, float length, float width, List<Vector3> resultsBuffer)
        {
            Vector3 axisEnd = axisStart + (axisDirection * length);
            AddQuads(axisStart, axisEnd, axisOtherDirection1, axisOtherDirection2, width, resultsBuffer);
        }
        void AddQuads(Vector3 axisStart, Vector3 axisEnd, Vector3 axisOtherDirection1, Vector3 axisOtherDirection2, float width, List<Vector3> resultsBuffer)
        {
            Square baseRectangle = GetBaseSquare(axisStart, axisOtherDirection1, axisOtherDirection2, width);
            Square baseRectangleEnd = GetBaseSquare(axisEnd, axisOtherDirection1, axisOtherDirection2, width);

            resultsBuffer.Add(baseRectangle.bottomLeft);
            resultsBuffer.Add(baseRectangle.topLeft);
            resultsBuffer.Add(baseRectangle.topRight);
            resultsBuffer.Add(baseRectangle.bottomRight);

            resultsBuffer.Add(baseRectangleEnd.bottomLeft);
            resultsBuffer.Add(baseRectangleEnd.topLeft);
            resultsBuffer.Add(baseRectangleEnd.topRight);
            resultsBuffer.Add(baseRectangleEnd.bottomRight);

            for (int i = 0; i < 4; i++)
            {
                resultsBuffer.Add(baseRectangle[i]);
                resultsBuffer.Add(baseRectangleEnd[i]);
                resultsBuffer.Add(baseRectangleEnd[i + 1]);
                resultsBuffer.Add(baseRectangle[i + 1]);
            }
        }

        void AddQuad(Vector3 axisStart, Vector3 axisOtherDirection1, Vector3 axisOtherDirection2, float width, List<Vector3> resultsBuffer)
        {
            Square baseRectangle = GetBaseSquare(axisStart, axisOtherDirection1, axisOtherDirection2, width);

            resultsBuffer.Add(baseRectangle.bottomLeft);
            resultsBuffer.Add(baseRectangle.topLeft);
            resultsBuffer.Add(baseRectangle.topRight);
            resultsBuffer.Add(baseRectangle.bottomRight);
        }

        Square GetBaseSquare(Vector3 axisEnd, Vector3 axisOtherDirection1, Vector3 axisOtherDirection2, float size)
        {
            Square square;
            Vector3 offsetUp = (axisOtherDirection1 * size) + (axisOtherDirection2 * size);
            Vector3 offsetDown = (axisOtherDirection1 * size) - (axisOtherDirection2 * size);
            // These might not really be the proper directions, as in the bottomLeft might not really be at the bottom left...
            square.bottomLeft = axisEnd + offsetDown;
            square.topLeft = axisEnd + offsetUp;
            square.bottomRight = axisEnd - offsetUp;
            square.topRight = axisEnd - offsetDown;
            return square;
        }

        void SetCircles(AxisInfo axisInfo, AxisVectors axisVectors)
        {
            axisVectors.Clear();

            if (TranslatingTypeContains(TransformType.Rotate))
            {
                float circleLength = GetHandleLength(TransformType.Rotate);
                AddCircle(PivotPoint, axisInfo.xDirection, circleLength, axisVectors.x);
                AddCircle(PivotPoint, axisInfo.yDirection, circleLength, axisVectors.y);
                AddCircle(PivotPoint, axisInfo.zDirection, circleLength, axisVectors.z);
                AddCircle(PivotPoint, (PivotPoint - transform.position).normalized, circleLength, axisVectors.all, false);
            }
        }

        void AddCircle(Vector3 origin, Vector3 axisDirection, float size, List<Vector3> resultsBuffer, bool depthTest = true)
        {
            Vector3 up = axisDirection.normalized * size;
            Vector3 forward = Vector3.Slerp(up, -up, .5f);
            Vector3 right = Vector3.Cross(up, forward).normalized * size;

            Matrix4x4 matrix = new();

            matrix[0] = right.x;
            matrix[1] = right.y;
            matrix[2] = right.z;

            matrix[4] = up.x;
            matrix[5] = up.y;
            matrix[6] = up.z;

            matrix[8] = forward.x;
            matrix[9] = forward.y;
            matrix[10] = forward.z;

            Vector3 lastPoint = origin + matrix.MultiplyPoint3x4(new Vector3(Mathf.Cos(0), 0, Mathf.Sin(0)));
            Vector3 nextPoint = Vector3.zero;
            float multiplier = 360f / circleDetail;

            Plane plane = new((transform.position - PivotPoint).normalized, PivotPoint);

            float circleHandleWidth = handleWidth * GetDistanceMultiplier();

            for (int i = 0; i < circleDetail + 1; i++)
            {
                nextPoint.x = Mathf.Cos(i * multiplier * Mathf.Deg2Rad);
                nextPoint.z = Mathf.Sin(i * multiplier * Mathf.Deg2Rad);
                nextPoint.y = 0;

                nextPoint = origin + matrix.MultiplyPoint3x4(nextPoint);

                if (!depthTest || plane.GetSide(lastPoint))
                {
                    Vector3 centerPoint = (lastPoint + nextPoint) * .5f;
                    Vector3 upDirection = (centerPoint - origin).normalized;
                    AddQuads(lastPoint, nextPoint, upDirection, axisDirection, circleHandleWidth, resultsBuffer);
                }

                lastPoint = nextPoint;
            }
        }

        private void SelectMoveToolCmd(InputAction.CallbackContext context)
        {
            SetTransformType(TransformType.Move);
        }
        private void SelectRotateToolCmd(InputAction.CallbackContext context)
        {
            SetTransformType(TransformType.Rotate);
        }
        private void SelectScaleToolCmd(InputAction.CallbackContext context)
        {
            SetTransformType(TransformType.Scale);

            if (pivot is TransformPivot.Pivot)
            {
                // FromPointOffset can be inaccurate and should only really be used in Center mode if desired.
                scaleType = ScaleType.FromPoint;
            }
        }
        private void SelectMultiToolCmd(InputAction.CallbackContext context)
        {
            SetTransformType(TransformType.All);
        }
        public void SetTransformType(TransformType newType)
        {
            OnTransformTypeChanging?.Invoke(new(transformType, newType));
            transformType = newType;
        }

        private void TogglePivotCmd(InputAction.CallbackContext context)
        {
            pivot = pivot switch
            {
                TransformPivot.Pivot => TransformPivot.Center,
                TransformPivot.Center => TransformPivot.Pivot,
                _ => throw new InvalidOperationException(),
            };

            SetPivotPoint();
        }

        private void ToggleCenterTypeCmd(InputAction.CallbackContext context)
        {
            centerType = centerType switch
            {
                CenterType.All => CenterType.Solo,
                CenterType.Solo => CenterType.All,
                _ => throw new InvalidOperationException(),
            };

            SetPivotPoint();
        }

        private void ToggleScaleTypeCmd(InputAction.CallbackContext context)
        {
            scaleType = scaleType switch
            {
                ScaleType.FromPoint => ScaleType.FromPointOffset,
                ScaleType.FromPointOffset => ScaleType.FromPoint,
                _ => throw new InvalidOperationException(),
            };
        }

        private void ToggleSpaceCmd(InputAction.CallbackContext context)
        {
            space = space switch
            {
                TransformSpace.Local => TransformSpace.Global,
                TransformSpace.Global => TransformSpace.Local,
                _ => throw new InvalidOperationException(),
            };
        }

        private void DeleteCmd(InputAction.CallbackContext context)
        {
            var cmd = new DeleteCommand(this, targetRootsOrdered);
            if (cmd.ObjectCount > 0)
            {
                UndoRedo.Global.Execute(cmd);
            }
        }

        private void UndoCmd(InputAction.CallbackContext context)
        {
            UndoRedo.Global.Undo();
        }
        private void RedoCmd(InputAction.CallbackContext context)
        {
            UndoRedo.Global.Redo();
        }

        private static void RegisterNullSafe(InputActionReference reference, Action<InputAction.CallbackContext> callback)
        {
            if (reference != null)
            {
                reference.action.performed += callback;
            }
        }
        private static void UnregisterNullSafe(InputActionReference reference, Action<InputAction.CallbackContext> callback)
        {
            if (reference != null)
            {
                reference.action.performed -= callback;
            }
        }

        void Reset()
        {
            camera = GetComponent<Camera>();
        }
    }

    public readonly struct TransformTypeChangingEventArgs
    {
        public TransformTypeChangingEventArgs(TransformType current, TransformType @new)
        {
            Current = current;
            New = @new;
        }

        public TransformType Current { get; }
        public TransformType New { get; }
    }
}

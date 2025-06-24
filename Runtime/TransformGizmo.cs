using System;
using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using CommandUndoRedo;
using RuntimeGizmos.Commands;

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

        // These are the same as the unity editor hotkeys
        public KeyCode SetMoveType = KeyCode.W;
        public KeyCode SetRotateType = KeyCode.E;
        public KeyCode SetScaleType = KeyCode.R;
        //public KeyCode SetRectToolType = KeyCode.T;
        public KeyCode SetAllTransformType = KeyCode.Y;
        public KeyCode SetSpaceToggle = KeyCode.X;
        public KeyCode SetPivotModeToggle = KeyCode.Z;
        public KeyCode SetCenterTypeToggle = KeyCode.C;
        public KeyCode SetScaleTypeToggle = KeyCode.S;
        public KeyCode translationSnapping = KeyCode.LeftControl;
        public KeyCode AddSelection = KeyCode.LeftShift;
        public KeyCode RemoveSelection = KeyCode.LeftControl;
        public KeyCode ActionKey = KeyCode.LeftControl; // you should disable unity shortcuts at runtime so they do not interfere
        public KeyCode UndoAction = KeyCode.Z;
        public KeyCode RedoAction = KeyCode.Y;

        public float movementSnap = .25f;
        public float rotationSnap = 15f;
        public float scaleSnap = 1f;

        public float handleLength = .25f;
        public float handleWidth = .003f;
        public float planeSize = .035f;
        public float triangleSize = .03f;
        public float boxSize = .03f;
        public int circleDetail = 40;
        public float allMoveHandleLengthMultiplier = 1f;
        public float allRotateHandleLengthMultiplier = 1.4f;
        public float allScaleHandleLengthMultiplier = 1.6f;
        public float minSelectedDistanceCheck = .01f;
        public float moveSpeedMultiplier = 1f;
        public float scaleSpeedMultiplier = 1f;
        public float rotateSpeedMultiplier = 1f;
        public float allRotateSpeedMultiplier = 20f;

        public bool useFirstSelectedAsMain = true;

        //If circularRotationMethod is true, when rotating you will need to move your mouse around the object as if turning a wheel.
        //If circularRotationMethod is false, when rotating you can just click and drag in a line to rotate.
        public bool circularRotationMethod;

        //Mainly for if you want the pivot point to update correctly if selected objects are moving outside the transformgizmo.
        //Might be poor on performance if lots of objects are selected...
        public bool forceUpdatePivotPointOnChange = true;

        [SerializeField] private int maxUndoStored = 100;

        public bool manuallyHandleGizmo;

        public LayerMask selectionMask = Physics.DefaultRaycastLayers;

        public Action onCheckForSelectedAxis;
        public Action onDrawCustomGizmo;

        [SerializeField] private new Camera camera;

        public bool IsTransforming { get; private set; }
        public float TotalScaleAmount { get; private set; }
        public Quaternion TotalRotationAmount { get; private set; }
        public Axis TranslatingAxis => nearAxis;
        public Axis TranslatingAxisPlane => planeAxis;
        public bool HasTranslatingAxisPlane => TranslatingAxisPlane != Axis.None && TranslatingAxisPlane != Axis.Any;
        public TransformType TransformingType => translatingType;

        public Vector3 PivotPoint { get; private set; }
        Vector3 totalCenterPivotPoint;

        public Transform MainTargetRoot => (targetRootsOrdered.Count > 0) ? useFirstSelectedAsMain ? targetRootsOrdered[0] : targetRootsOrdered[^1] : null;

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
        readonly List<Transform> targetRootsOrdered = new();
        readonly Dictionary<Transform, TargetInfo> targetRoots = new();
        public readonly HashSet<Renderer> highlightedRenderers = new();
        readonly HashSet<Transform> children = new();

        readonly List<Transform> childrenBuffer = new();
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
        }

        void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            ClearTargets(); // Just so things gets cleaned up, such as removing any materials we placed on objects.
        }

        void OnDestroy()
        {
            ClearAllHighlightedRenderers();
        }

        void Update()
        {
            HandleUndoRedo();

            SetSpaceAndType();

            if (manuallyHandleGizmo)
            {
                if (onCheckForSelectedAxis != null) onCheckForSelectedAxis();
            }
            else
            {
                SetNearAxis();
            }

            GetTarget();

            if (MainTargetRoot == null) return;

            TransformSelected();
        }

        void LateUpdate()
        {
            if (MainTargetRoot == null) return;

            //We run this in lateupdate since coroutines run after update and we want our gizmos to have the updated target transform position after TransformSelected()
            SetAxisInfo();

            if (manuallyHandleGizmo)
            {
                onDrawCustomGizmo?.Invoke();
            }
            else
            {
                SetLines();
            }
        }

        void HandleUndoRedo()
        {
            if (maxUndoStored != UndoRedo.Global.MaxUndoStored)
            {
                UndoRedo.Global.MaxUndoStored = maxUndoStored;
            }

            if (Input.GetKey(ActionKey))
            {
                if (Input.GetKeyDown(UndoAction))
                {
                    UndoRedo.Global.Undo();
                }
                else if (Input.GetKeyDown(RedoAction))
                {
                    UndoRedo.Global.Redo();
                }
            }
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
            if (transformType == TransformType.All)
            {
                if (type == TransformType.Move) length *= allMoveHandleLengthMultiplier;
                if (type == TransformType.Rotate) length *= allRotateHandleLengthMultiplier;
                if (type == TransformType.Scale) length *= allScaleHandleLengthMultiplier;
            }

            if (multiplyDistanceMultiplier) length *= GetDistanceMultiplier();

            if (type == TransformType.Scale && IsTransforming && (TranslatingAxis == axis || TranslatingAxis == Axis.Any)) length += TotalScaleAmount;

            return length;
        }

        void SetSpaceAndType()
        {
            if (Input.GetKey(ActionKey)) return;

            if (Input.GetKeyDown(SetMoveType)) transformType = TransformType.Move;
            else if (Input.GetKeyDown(SetRotateType)) transformType = TransformType.Rotate;
            else if (Input.GetKeyDown(SetScaleType)) transformType = TransformType.Scale;
            //else if(Input.GetKeyDown(SetRectToolType)) type = TransformType.RectTool;
            else if (Input.GetKeyDown(SetAllTransformType)) transformType = TransformType.All;

            if (!IsTransforming) translatingType = transformType;

            if (Input.GetKeyDown(SetPivotModeToggle))
            {
                if (pivot == TransformPivot.Pivot) pivot = TransformPivot.Center;
                else if (pivot == TransformPivot.Center) pivot = TransformPivot.Pivot;

                SetPivotPoint();
            }

            if (Input.GetKeyDown(SetCenterTypeToggle))
            {
                if (centerType == CenterType.All) centerType = CenterType.Solo;
                else if (centerType == CenterType.Solo) centerType = CenterType.All;

                SetPivotPoint();
            }

            if (Input.GetKeyDown(SetSpaceToggle))
            {
                if (space == TransformSpace.Global) space = TransformSpace.Local;
                else if (space == TransformSpace.Local) space = TransformSpace.Global;
            }

            if (Input.GetKeyDown(SetScaleTypeToggle))
            {
                if (scaleType == ScaleType.FromPoint) scaleType = ScaleType.FromPointOffset;
                else if (scaleType == ScaleType.FromPointOffset) scaleType = ScaleType.FromPoint;
            }

            if (transformType == TransformType.Scale)
            {
                if (pivot == TransformPivot.Pivot) scaleType = ScaleType.FromPoint; //FromPointOffset can be inaccurate and should only really be used in Center mode if desired.
            }
        }

        void TransformSelected()
        {
            if (MainTargetRoot != null)
            {
                if (nearAxis != Axis.None && Input.GetMouseButtonDown(0))
                {
                    StartCoroutine(TransformSelected(translatingType));
                }
            }
        }

        IEnumerator TransformSelected(TransformType transType)
        {
            IsTransforming = true;
            TotalScaleAmount = 0;
            TotalRotationAmount = Quaternion.identity;

            Vector3 originalPivot = PivotPoint;

            Vector3 otherAxis1, otherAxis2;
            Vector3 axis = GetNearAxisDirection(out otherAxis1, out otherAxis2);
            Vector3 planeNormal = HasTranslatingAxisPlane ? axis : (transform.position - originalPivot).normalized;
            Vector3 projectedAxis = Vector3.ProjectOnPlane(axis, planeNormal).normalized;
            Vector3 previousMousePosition = Vector3.zero;

            Vector3 currentSnapMovementAmount = Vector3.zero;
            float currentSnapRotationAmount = 0;
            float currentSnapScaleAmount = 0;

            List<ICommand> transformCommands = new();
            for (int i = 0; i < targetRootsOrdered.Count; i++)
            {
                transformCommands.Add(new TransformCommand(this, targetRootsOrdered[i]));
            }

            while (!Input.GetMouseButtonUp(0))
            {
                Ray mouseRay = camera.ScreenPointToRay(Input.mousePosition);
                Vector3 mousePosition = Geometry.LinePlaneIntersect(mouseRay.origin, mouseRay.direction, originalPivot, planeNormal);
                bool isSnapping = Input.GetKey(translationSnapping);

                if (previousMousePosition != Vector3.zero && mousePosition != Vector3.zero)
                {
                    if (transType == TransformType.Move)
                    {
                        Vector3 movement = Vector3.zero;

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
                            Transform target = targetRootsOrdered[i];

                            target.Translate(movement, Space.World);
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

                            float remainder;
                            float snapAmount = CalculateSnapAmount(scaleSnap, currentSnapScaleAmount, out remainder);

                            if (snapAmount != 0)
                            {
                                scaleAmount = snapAmount;
                                currentSnapScaleAmount = remainder;
                            }
                        }

                        // WARNING - There is a bug in unity 5.4 and 5.5 that causes InverseTransformDirection to be affected by scale which will break negative scaling. Not tested, but updating to 5.4.2 should fix it - https://issuetracker.unity3d.com/issues/transformdirection-and-inversetransformdirection-operations-are-affected-by-scale
                        Vector3 localAxis = (GetProperTransformSpace() == TransformSpace.Local && nearAxis != Axis.Any) ? MainTargetRoot.InverseTransformDirection(axis) : axis;
                        Vector3 targetScaleAmount;
                        if (nearAxis == Axis.Any) targetScaleAmount = ExtVector3.Abs(MainTargetRoot.localScale.normalized) * scaleAmount;
                        else targetScaleAmount = localAxis * scaleAmount;

                        for (int i = 0; i < targetRootsOrdered.Count; i++)
                        {
                            Transform target = targetRootsOrdered[i];

                            Vector3 targetScale = target.localScale + targetScaleAmount;

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
                        float rotateAmount = 0;
                        Vector3 rotationAxis = axis;

                        if (nearAxis == Axis.Any)
                        {
                            Vector3 rotation = transform.TransformDirection(new Vector3(Input.GetAxis("Mouse Y"), -Input.GetAxis("Mouse X"), 0));
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

                            float remainder;
                            float snapAmount = CalculateSnapAmount(rotationSnap, currentSnapRotationAmount, out remainder);

                            if (snapAmount != 0)
                            {
                                rotateAmount = snapAmount;
                                currentSnapRotationAmount = remainder;
                            }
                        }

                        for (int i = 0; i < targetRootsOrdered.Count; i++)
                        {
                            Transform target = targetRootsOrdered[i];

                            if (pivot == TransformPivot.Pivot)
                            {
                                target.Rotate(rotationAxis, rotateAmount, Space.World);
                            }
                            else if (pivot == TransformPivot.Center)
                            {
                                target.RotateAround(originalPivot, rotationAxis, rotateAmount);
                            }
                        }

                        TotalRotationAmount *= Quaternion.Euler(rotationAxis * rotateAmount);
                    }
                }

                previousMousePosition = mousePosition;

                yield return null;
            }

            for (int i = 0; i < transformCommands.Count; i++)
            {
                ((TransformCommand)transformCommands[i]).StoreNewTransformValues();
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

        float CalculateSnapAmount(float snapValue, float currentAmount, out float remainder)
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

        Vector3 GetNearAxisDirection(out Vector3 otherAxis1, out Vector3 otherAxis2)
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

        void GetTarget()
        {
            if (nearAxis == Axis.None && Input.GetMouseButtonDown(0))
            {
                bool isAdding = Input.GetKey(AddSelection);
                bool isRemoving = Input.GetKey(RemoveSelection);

                if (Physics.Raycast(camera.ScreenPointToRay(Input.mousePosition), out RaycastHit hitInfo, Mathf.Infinity, selectionMask))
                {
                    var obj = hitInfo.collider.GetComponentInParent<RuntimeEditor>();
                    if (obj != null)
                    {

                        Transform target = obj.transform;

                        if (isAdding)
                        {
                            AddTarget(target);
                        }
                        else if (isRemoving)
                        {
                            RemoveTarget(target);
                        }
                        else if (!isAdding && !isRemoving)
                        {
                            ClearAndAddTarget(target);
                        }

                        return;
                    }
                }

                if (!isAdding && !isRemoving)
                {
                    ClearTargets();
                }
            }
        }

        public void AddTarget(Transform target, bool addCommand = true)
        {
            if (target != null)
            {
                if (targetRoots.ContainsKey(target)) return;
                if (children.Contains(target)) return;

                if (addCommand) UndoRedo.Global.Insert(new AddTargetCommand(this, target, targetRootsOrdered));

                AddTargetRoot(target);
                AddTargetHighlightedRenderers(target);

                SetPivotPoint();
            }
        }

        public void RemoveTarget(Transform target, bool addCommand = true)
        {
            if (target != null)
            {
                if (!targetRoots.ContainsKey(target)) return;

                if (addCommand) UndoRedo.Global.Insert(new RemoveTargetCommand(this, target));

                RemoveTargetHighlightedRenderers(target);
                RemoveTargetRoot(target);

                SetPivotPoint();
            }
        }

        public void ClearTargets(bool addCommand = true)
        {
            if (addCommand) UndoRedo.Global.Insert(new ClearTargetsCommand(this, targetRootsOrdered));

            ClearAllHighlightedRenderers();
            targetRoots.Clear();
            targetRootsOrdered.Clear();
            children.Clear();
        }

        void ClearAndAddTarget(Transform target)
        {
            UndoRedo.Global.Insert(new ClearAndAddTargetCommand(this, target, targetRootsOrdered));

            ClearTargets(false);
            AddTarget(target, false);
        }

        void AddTargetHighlightedRenderers(Transform target)
        {
            if (target != null)
            {
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
        }

        void GetTargetRenderers(Transform target, List<Renderer> renderers)
        {
            renderers.Clear();
            if (target != null)
            {
                target.GetComponentsInChildren<Renderer>(true, renderers);
            }
        }

        void ClearAllHighlightedRenderers()
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

        void RemoveTargetHighlightedRenderers(Transform target)
        {
            GetTargetRenderers(target, renderersBuffer);

            RemoveHighlightedRenderers(renderersBuffer);
        }

        void RemoveHighlightedRenderers(List<Renderer> renderers)
        {
            for (int i = 0; i < renderersBuffer.Count; i++)
            {
                highlightedRenderers.Remove(renderersBuffer[i]);
            }

            renderersBuffer.Clear();
        }

        void AddTargetRoot(Transform targetRoot)
        {
            targetRoots.Add(targetRoot, new TargetInfo());
            targetRootsOrdered.Add(targetRoot);

            AddAllChildren(targetRoot);
        }
        void RemoveTargetRoot(Transform targetRoot)
        {
            if (targetRoots.Remove(targetRoot))
            {
                targetRootsOrdered.Remove(targetRoot);

                RemoveAllChildren(targetRoot);
            }
        }

        void AddAllChildren(Transform target)
        {
            childrenBuffer.Clear();
            target.GetComponentsInChildren<Transform>(true, childrenBuffer);
            childrenBuffer.Remove(target);

            for (int i = 0; i < childrenBuffer.Count; i++)
            {
                Transform child = childrenBuffer[i];
                children.Add(child);
                RemoveTargetRoot(child); //We do this in case we selected child first and then the parent.
            }

            childrenBuffer.Clear();
        }
        void RemoveAllChildren(Transform target)
        {
            childrenBuffer.Clear();
            target.GetComponentsInChildren<Transform>(true, childrenBuffer);
            childrenBuffer.Remove(target);

            for (int i = 0; i < childrenBuffer.Count; i++)
            {
                children.Remove(childrenBuffer[i]);
            }

            childrenBuffer.Clear();
        }

        public void SetPivotPoint()
        {
            if (MainTargetRoot != null)
            {
                if (pivot == TransformPivot.Pivot)
                {
                    PivotPoint = MainTargetRoot.position;
                }
                else if (pivot == TransformPivot.Center)
                {
                    totalCenterPivotPoint = Vector3.zero;

                    Dictionary<Transform, TargetInfo>.Enumerator targetsEnumerator = targetRoots.GetEnumerator();
                    while (targetsEnumerator.MoveNext())
                    {
                        Transform target = targetsEnumerator.Current.Key;
                        TargetInfo info = targetsEnumerator.Current.Value;
                        info.centerPivotPoint = target.GetCenter(centerType);

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

        void SetNearAxis()
        {
            if (IsTransforming) return;

            SetTranslatingAxis(transformType, Axis.None);

            if (MainTargetRoot == null) return;

            float distanceMultiplier = GetDistanceMultiplier();
            float handleMinSelectedDistanceCheck = (this.minSelectedDistanceCheck + handleWidth) * distanceMultiplier;

            if (nearAxis == Axis.None && (TransformTypeContains(TransformType.Move) || TransformTypeContains(TransformType.Scale)))
            {
                //Important to check scale lines before move lines since in TransformType.All the move planes would block the scales center scale all gizmo.
                if (nearAxis == Axis.None && TransformTypeContains(TransformType.Scale))
                {
                    float tipMinSelectedDistanceCheck = (this.minSelectedDistanceCheck + boxSize) * distanceMultiplier;
                    HandleNearestPlanes(TransformType.Scale, handleSquares, tipMinSelectedDistanceCheck);
                }

                if (nearAxis == Axis.None && TransformTypeContains(TransformType.Move))
                {
                    //Important to check the planes first before the handle tip since it makes selecting the planes easier.
                    float planeMinSelectedDistanceCheck = (this.minSelectedDistanceCheck + planeSize) * distanceMultiplier;
                    HandleNearestPlanes(TransformType.Move, handlePlanes, planeMinSelectedDistanceCheck);

                    if (nearAxis != Axis.None)
                    {
                        planeAxis = nearAxis;
                    }
                    else
                    {
                        float tipMinSelectedDistanceCheck = (this.minSelectedDistanceCheck + triangleSize) * distanceMultiplier;
                        HandleNearestLines(TransformType.Move, handleTriangles, tipMinSelectedDistanceCheck);
                    }
                }

                if (nearAxis == Axis.None)
                {
                    //Since Move and Scale share the same handle line, we give Move the priority.
                    TransformType transType = transformType == TransformType.All ? TransformType.Move : transformType;
                    HandleNearestLines(transType, handleLines, handleMinSelectedDistanceCheck);
                }
            }

            if (nearAxis == Axis.None && TransformTypeContains(TransformType.Rotate))
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
                Ray mouseRay = camera.ScreenPointToRay(Input.mousePosition);
                Vector3 mousePlaneHit = Geometry.LinePlaneIntersect(mouseRay.origin, mouseRay.direction, PivotPoint, (transform.position - PivotPoint).normalized);
                if ((PivotPoint - mousePlaneHit).sqrMagnitude <= GetHandleLength(TransformType.Rotate).Squared()) SetTranslatingAxis(type, Axis.Any);
            }
        }

        float ClosestDistanceFromMouseToLines(List<Vector3> lines)
        {
            Ray mouseRay = camera.ScreenPointToRay(Input.mousePosition);

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
                Ray mouseRay = camera.ScreenPointToRay(Input.mousePosition);

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
            if (MainTargetRoot != null)
            {
                axisInfo.Set(MainTargetRoot, PivotPoint, GetProperTransformSpace());
            }
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

        void Reset()
        {
            camera = GetComponent<Camera>();
        }
    }
}

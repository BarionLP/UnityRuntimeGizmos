using CommandUndoRedo;
using UnityEngine;

namespace RuntimeGizmos.Commands
{
	public class TransformCommand : ICommand
	{
		TransformValues newValues;
		TransformValues oldValues;

		readonly Transform transform;
		readonly TransformGizmo transformGizmo;

		public TransformCommand(TransformGizmo transformGizmo, Transform transform)
		{
			this.transformGizmo = transformGizmo;
			this.transform = transform;

			oldValues = new TransformValues() { position = transform.position, rotation = transform.rotation, scale = transform.localScale };
		}

		public void StoreNewTransformValues()
		{
			newValues = new TransformValues() { position = transform.position, rotation = transform.rotation, scale = transform.localScale };
		}

		public void Execute()
		{
			transform.SetPositionAndRotation(newValues.position, newValues.rotation);
			transform.localScale = newValues.scale;

			transformGizmo.SetPivotPoint();
		}

		public void UnExecute()
		{
			transform.SetPositionAndRotation(oldValues.position, oldValues.rotation);
			transform.localScale = oldValues.scale;

			transformGizmo.SetPivotPoint();
		}

		struct TransformValues
		{
			public Vector3 position;
			public Quaternion rotation;
			public Vector3 scale;
		}
	}
}

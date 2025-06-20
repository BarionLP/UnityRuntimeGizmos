using UnityEngine;

namespace RuntimeGizmos
{
	public struct AxisInfo
	{
		public Vector3 pivot;
		public Vector3 xDirection;
		public Vector3 yDirection;
		public Vector3 zDirection;

		public void Set(Transform target, Vector3 pivot, TransformSpace space)
		{
			if (space == TransformSpace.Global)
			{
				xDirection = Vector3.right;
				yDirection = Vector3.up;
				zDirection = Vector3.forward;
			}
			else if (space == TransformSpace.Local)
			{
				xDirection = target.right;
				yDirection = target.up;
				zDirection = target.forward;
			}

			this.pivot = pivot;
		}

        public readonly Vector3 GetXAxisEnd(float size) => pivot + (xDirection * size);
        public readonly Vector3 GetYAxisEnd(float size) => pivot + (yDirection * size);
        public readonly Vector3 GetZAxisEnd(float size) => pivot + (zDirection * size);
        public readonly Vector3 GetAxisEnd(Vector3 direction, float size) => pivot + (direction * size);
    }
}

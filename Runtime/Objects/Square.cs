using UnityEngine;

namespace RuntimeGizmos
{
	public struct Square
	{
		public Vector3 bottomLeft;
		public Vector3 bottomRight;
		public Vector3 topLeft;
		public Vector3 topRight;

		public readonly Vector3 this[int index] => index switch
		{
			0 => bottomLeft,
			1 => topLeft,
			2 => topRight,
			3 => bottomRight,
			4 => bottomLeft, // so we wrap around back to start
			_ => Vector3.zero,
		};
	}
}

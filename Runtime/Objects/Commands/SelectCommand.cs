using CommandUndoRedo;
using UnityEngine;

namespace RuntimeGizmos.Commands
{
	public abstract class SelectCommand : ICommand
	{
		protected RuntimeEditable target;
		protected TransformGizmo transformGizmo;

		public SelectCommand(TransformGizmo transformGizmo, RuntimeEditable target)
		{
			this.transformGizmo = transformGizmo;
			this.target = target;
		}

		public abstract void Execute();
		public abstract void UnExecute();
	}
}

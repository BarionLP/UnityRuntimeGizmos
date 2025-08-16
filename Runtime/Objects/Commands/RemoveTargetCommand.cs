using UnityEngine;

namespace RuntimeGizmos.Commands
{
    public sealed class RemoveTargetCommand : SelectCommand
	{
		public RemoveTargetCommand(TransformGizmo transformGizmo, RuntimeEditable target) : base(transformGizmo, target) { }

		public override void Execute()
		{
			transformGizmo.RemoveTarget(target, addCommand: false);
		}

		public override void UnExecute()
		{
			transformGizmo.AddTarget(target, addCommand: false);
		}
	}
}

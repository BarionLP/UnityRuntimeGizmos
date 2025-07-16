using UnityEngine;

namespace RuntimeGizmos.Commands
{
    public sealed class RemoveTargetCommand : SelectCommand
	{
		public RemoveTargetCommand(TransformGizmo transformGizmo, Transform target) : base(transformGizmo, target) { }

		public override void Execute()
		{
			transformGizmo.RemoveTarget(target, false);
		}

		public override void UnExecute()
		{
			transformGizmo.AddTarget(target, false);
		}
	}
}

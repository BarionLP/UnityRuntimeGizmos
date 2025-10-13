namespace RuntimeGizmos.Commands
{
    public sealed class RemoveTargetCommand : SelectCommand
	{
		public RemoveTargetCommand(TransformGizmo transformGizmo, RuntimeEditable target) : base(transformGizmo, target) { }

		public override void Execute()
		{
			transformGizmo.ExecuteRemoveTarget(target);
		}

		public override void UnExecute()
		{
			transformGizmo.ExecuteAddTarget(target);
		}
	}
}

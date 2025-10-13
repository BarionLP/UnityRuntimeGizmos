using System.Collections.Generic;

namespace RuntimeGizmos.Commands
{
    public sealed class AddTargetCommand : SelectCommand
	{
		private readonly List<RuntimeEditable> targetRoots = new ();

		public AddTargetCommand(TransformGizmo transformGizmo, RuntimeEditable target, List<RuntimeEditable> targetRoots) : base(transformGizmo, target)
		{
			// Since we might have had a child selected and then selected the parent, the child would have been removed from the selected,
			// so we store all the targetRoots before we add so that if we undo we can properly have the children selected again.
			this.targetRoots.AddRange(targetRoots);
		}

		public override void Execute()
		{
			transformGizmo.ExecuteAddTarget(target);
		}

		public override void UnExecute()
		{
			transformGizmo.ExecuteRemoveTarget(target);

			for (int i = 0; i < targetRoots.Count; i++)
			{
				transformGizmo.ExecuteAddTarget(targetRoots[i]);
			}
		}
	}
}

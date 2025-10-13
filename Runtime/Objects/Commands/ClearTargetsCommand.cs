using System.Collections.Generic;
using CommandUndoRedo;

namespace RuntimeGizmos.Commands
{
    public sealed class ClearTargetsCommand : ICommand
	{
		private readonly List<RuntimeEditable> targetRoots = new();
        private readonly TransformGizmo transformGizmo;

        public ClearTargetsCommand(TransformGizmo transformGizmo, IEnumerable<RuntimeEditable> targetRoots)
		{
			this.targetRoots.AddRange(targetRoots);
            this.transformGizmo = transformGizmo;
        }

		public void Execute()
		{
			transformGizmo.ExecuteClearTargets();
		}

		public void UnExecute()
		{
			for (int i = 0; i < targetRoots.Count; i++)
			{
				transformGizmo.ExecuteAddTarget(targetRoots[i]);
			}
		}
	}
}

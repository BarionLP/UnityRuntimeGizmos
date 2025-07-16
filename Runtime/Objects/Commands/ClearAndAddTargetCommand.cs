using UnityEngine;
using System.Collections.Generic;

namespace RuntimeGizmos.Commands
{
    public sealed class ClearAndAddTargetCommand : SelectCommand
	{
		private readonly List<Transform> targetRoots = new();

		public ClearAndAddTargetCommand(TransformGizmo transformGizmo, Transform target, List<Transform> targetRoots) : base(transformGizmo, target)
		{
			this.targetRoots.AddRange(targetRoots);
		}

		public override void Execute()
		{
			transformGizmo.ClearTargets(false);
			transformGizmo.AddTarget(target, false);
		}

		public override void UnExecute()
		{
			transformGizmo.RemoveTarget(target, false);

			for (int i = 0; i < targetRoots.Count; i++)
			{
				transformGizmo.AddTarget(targetRoots[i], false);
			}
		}
	}
}

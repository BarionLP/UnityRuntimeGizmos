using UnityEngine;
using System.Collections.Generic;

namespace RuntimeGizmos.Commands
{
    public sealed class ClearTargetsCommand : SelectCommand
	{
		private readonly List<Transform> targetRoots = new();

		public ClearTargetsCommand(TransformGizmo transformGizmo, List<Transform> targetRoots) : base(transformGizmo, null)
		{
			this.targetRoots.AddRange(targetRoots);
		}

		public override void Execute()
		{
			transformGizmo.ClearTargets(false);
		}

		public override void UnExecute()
		{
			for (int i = 0; i < targetRoots.Count; i++)
			{
				transformGizmo.AddTarget(targetRoots[i], false);
			}
		}
	}
}

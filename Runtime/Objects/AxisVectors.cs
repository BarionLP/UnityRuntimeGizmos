using System.Collections.Generic;
using UnityEngine;

namespace RuntimeGizmos
{
	public class AxisVectors
	{
		public List<Vector3> x = new();
		public List<Vector3> y = new();
		public List<Vector3> z = new();
		public List<Vector3> all = new();

		public void Add(AxisVectors axisVectors)
		{
			x.AddRange(axisVectors.x);
			y.AddRange(axisVectors.y);
			z.AddRange(axisVectors.z);
			all.AddRange(axisVectors.all);
		}

		public void Clear()
		{
			x.Clear();
			y.Clear();
			z.Clear();
			all.Clear();
		}
	}
}
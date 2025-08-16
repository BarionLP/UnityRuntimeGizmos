using System;
using System.Collections.Generic;
using System.Linq;
using CommandUndoRedo;
using UnityEngine;

namespace RuntimeGizmos.Commands
{
    public sealed class DeleteCommand : ICommand
    {
        private readonly TransformGizmo transformGizmo;
        private readonly List<RuntimeEditable> objects;
        private bool isExecuted = false;
        public int ObjectCount => objects.Count;

        public DeleteCommand(TransformGizmo transformGizmo, IEnumerable<RuntimeEditable> selected)
        {
            objects = selected.Where(static o => o.Deletable).ToList();
            this.transformGizmo = transformGizmo;
        }

        public void Execute()
        {
            foreach (var obj in objects)
            {
                obj.MarkDeleted();
                obj.gameObject.SetActive(false);
                transformGizmo.RemoveTarget(obj, addCommand: false);
            }
            isExecuted = true;
        }

        public void UnExecute()
        {
            foreach (var obj in objects)
            {
                obj.Restore();
                obj.gameObject.SetActive(true);
                transformGizmo.AddTarget(obj, addCommand: false);
            }
            isExecuted = false;
        }

        public void CleanUp()
        {
            if (isExecuted)
            {
                foreach (var obj in objects)
                {
                    UnityEngine.Object.Destroy(obj.gameObject);
                }
            }
            objects.Clear();
        }
    }
}

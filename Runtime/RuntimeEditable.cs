using System;
using UnityEngine;

namespace RuntimeGizmos
{
    public sealed class RuntimeEditable : MonoBehaviour
    {
        [field: SerializeField] public bool Deletable { get; private set; } = false;
        public bool IsDeleted { get; private set; }
        public void MarkDeleted()
        {
            if (!Deletable) throw new InvalidOperationException($"{name} cannot be deleted");
            IsDeleted = true;
        }

        public void Restore() => IsDeleted = false;
    }
}

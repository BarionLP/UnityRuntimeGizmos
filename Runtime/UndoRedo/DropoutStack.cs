using System.Collections.Generic;

namespace CommandUndoRedo
{
	internal sealed class DropoutStack<T> : LinkedList<T>
	{
		int _maxLength = int.MaxValue;
		public int MaxLength
        {
            get => _maxLength;
            set
            {
                _maxLength = value;

                if (Count > _maxLength)
                {
                    int leftover = Count - _maxLength;
                    for (int i = 0; i < leftover; i++)
                    {
                        RemoveLast();
                    }
                }
            }
        }

        public DropoutStack(int maxLength = int.MaxValue)
		{
			MaxLength = maxLength;
		}

		public void Push(T item)
		{
			if (Count > 0 && Count + 1 > MaxLength)
			{
				RemoveLast();
			}

			if (Count + 1 <= MaxLength)
			{
				AddFirst(item);
			}
		}

		public T Pop()
		{
			T item = First.Value;
			RemoveFirst();
			return item;
		}
	}
}

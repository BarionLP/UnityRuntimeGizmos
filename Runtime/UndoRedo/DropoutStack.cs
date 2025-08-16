using System;
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
					var leftover = Count - _maxLength;
					for (int i = 0; i < leftover; i++)
					{
						OnDropOut?.Invoke(Last.Value);
						RemoveLast();
					}
				}
			}
		}

		public event Action<T> OnDropOut;

		public DropoutStack(int maxLength = int.MaxValue)
		{
			MaxLength = maxLength;
		}

		public void Push(T item)
		{
			if (Count > 0 && Count + 1 > MaxLength)
			{
				OnDropOut?.Invoke(Last.Value);
				RemoveLast();
			}

			if (Count + 1 <= MaxLength)
			{
				AddFirst(item);
			}
		}

		public T Pop()
		{
			var item = First.Value;
			RemoveFirst();
			return item;
		}		
	}
}

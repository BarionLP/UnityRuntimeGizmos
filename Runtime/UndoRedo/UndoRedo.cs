namespace CommandUndoRedo
{
	internal class UndoRedo
	{
		public int MaxUndoStored {get {return undoCommands.MaxLength;} set {SetMaxLength(value);}}

        readonly DropoutStack<ICommand> undoCommands = new DropoutStack<ICommand>();
        readonly DropoutStack<ICommand> redoCommands = new DropoutStack<ICommand>();

		public UndoRedo() {}
		public UndoRedo(int maxUndoStored)
		{
			this.MaxUndoStored = maxUndoStored;
		}

		public void Clear()
		{
			undoCommands.Clear();
			redoCommands.Clear();
		}

		public void Undo()
		{
			if(undoCommands.Count > 0)
			{
				ICommand command = undoCommands.Pop();
				command.UnExecute();
				redoCommands.Push(command);
			}
		}

		public void Redo()
		{
			if(redoCommands.Count > 0)
			{
				ICommand command = redoCommands.Pop();
				command.Execute();
				undoCommands.Push(command);
			}
		}

		public void Insert(ICommand command)
		{
			if(MaxUndoStored <= 0) return;

			undoCommands.Push(command);
			redoCommands.Clear();
		}

		public void Execute(ICommand command)
		{
			command.Execute();
			Insert(command);
		}

		void SetMaxLength(int max)
		{
			undoCommands.MaxLength = max;
			redoCommands.MaxLength = max;
		}
	}
}

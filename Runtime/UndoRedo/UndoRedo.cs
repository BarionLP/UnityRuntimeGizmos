namespace CommandUndoRedo
{
    internal sealed class UndoRedo
    {
        public static UndoRedo Global { get; } = new(128);
        public int MaxUndoStored
        {
            get => undoCommands.MaxLength;
            set
            {
                undoCommands.MaxLength = value;
                redoCommands.MaxLength = value;
            }
        }

        private readonly DropoutStack<ICommand> undoCommands = new();
        private readonly DropoutStack<ICommand> redoCommands = new();

        public UndoRedo(int maxUndoStored)
        {
            MaxUndoStored = maxUndoStored;
        }

        public void Execute(ICommand command)
        {
            command.Execute();
            Insert(command);
        }

        public void Insert(ICommand command)
        {
            if (MaxUndoStored <= 0) return;

            undoCommands.Push(command);
            redoCommands.Clear();
        }

        public void Undo()
        {
            if (undoCommands.Count > 0)
            {
                var command = undoCommands.Pop();
                command.UnExecute();
                redoCommands.Push(command);
            }
        }

        public void Redo()
        {
            if (redoCommands.Count > 0)
            {
                var command = redoCommands.Pop();
                command.Execute();
                undoCommands.Push(command);
            }
        }

        public void Clear()
        {
            undoCommands.Clear();
            redoCommands.Clear();
        }
    }
}

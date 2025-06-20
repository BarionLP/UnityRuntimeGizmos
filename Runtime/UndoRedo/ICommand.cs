namespace CommandUndoRedo
{
	internal interface ICommand
	{
		void Execute();
		void UnExecute();
	}
}

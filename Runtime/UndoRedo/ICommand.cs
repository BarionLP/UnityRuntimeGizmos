namespace CommandUndoRedo
{
	public interface ICommand
	{
		public void Execute();
		public void UnExecute();
		public void CleanUp() { }
	}
}

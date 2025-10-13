using System;
using System.Collections.Generic;

namespace CommandUndoRedo
{
	internal sealed class CommandGroup : ICommand
	{
		private readonly List<ICommand> commands = new();

		public CommandGroup(IEnumerable<ICommand> commands)
		{
			var i = 0;
			foreach(var command in commands)
			{
				if (command is null) throw new ArgumentNullException(nameof(commands), $"commands[{i}] was null");
				this.commands.Add(command);
				i++;
			}
		}

		public void Execute()
		{
			for(int i = 0; i < commands.Count; i++)
			{
				commands[i].Execute();
			}
		}

		public void UnExecute()
		{
			for(int i = commands.Count - 1; i >= 0; i--)
			{
				commands[i].UnExecute();
			}
		}
	}
}

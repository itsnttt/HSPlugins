using System.Collections.Generic;
using System.Linq;

namespace TheBirdOfHermes.Undo
{
    public class CompositeUndoCommand : IUndoCommand
    {
        public string Description { get; }
        private readonly List<IUndoCommand> _commands;

        public CompositeUndoCommand(string description, IEnumerable<IUndoCommand> commands)
        {
            Description = description;
            _commands = commands.ToList();
        }

        public void Undo()
        {
            for (int i = _commands.Count - 1; i >= 0; i--)
                _commands[i].Undo();
        }

        public void Redo()
        {
            foreach (var cmd in _commands)
                cmd.Redo();
        }
    }
}

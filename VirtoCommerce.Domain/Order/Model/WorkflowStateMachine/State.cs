using System;

namespace VirtoCommerce.Domain.Order.Model.WorkflowStateMachine
{
    public sealed class State : IEquatable<State>
    {
        public string Name { get; }
        public string Description { get; }

        public State(string name, string description = null)
        {
            Name = name;
            Description = description;
        }

        public override string ToString() => Name;

        public override int GetHashCode() => Name.GetHashCode();

        public override bool Equals(object obj)
        {
            if (obj is State state)
                return Equals(state);

            return false;
        }

        public bool Equals(State other)
        {
            if (other is null)
                return false;

            return Name == other.Name;
        }

        public static bool operator ==(State state1, State state2) =>
            state1?.Equals(state2) ?? state2 is null;

        public static bool operator !=(State state1, State state2) =>
            !(state1 == state2);

        public static implicit operator string(State state) =>
            state?.Name;
    }
}

using System;

namespace VirtoCommerce.Domain.Order.Model.WorkflowStateMachine
{
    public sealed class Trigger : IEquatable<Trigger>
    {
        public string Name { get; }
        public string ToState { get; }
        public string Description { get; }

        public Trigger(string name, string toState, string description = null)
        {
            Name = name;
            ToState = toState;
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

        public bool Equals(Trigger other)
        {
            if (other is null)
                return false;

            return Name == other.Name;
        }

        public static bool operator ==(Trigger trigger1, Trigger trigger2) =>
            trigger1?.Equals(trigger2) ?? trigger2 is null;

        public static bool operator !=(Trigger trigger1, Trigger trigger2) =>
            !(trigger1 == trigger2);

        public static implicit operator string(Trigger trigger) =>
            trigger?.Name;
    }
}

using System.Collections.Generic;
using VirtoCommerce.Domain.Order.Model.WorkflowStateMachine;
using VirtoCommerce.Platform.Core.Security;

namespace VirtoCommerce.Domain.Order.Services
{
    public interface IWorkflowStateMachineService
    {
        void Validate(string workflowJson);

        IWorkflowStateMachine CreateStateMachine(string workflowId, string initialState = null);
    }

    public interface IWorkflowStateMachine
    {
        State State { get; }

        IEnumerable<Trigger> GetPermittedTriggers(ApplicationUserExtended user);

        void Fire(ApplicationUserExtended user, string trigger);
    }
}

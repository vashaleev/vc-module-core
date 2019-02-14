using VirtoCommerce.Domain.Order.Model;

namespace VirtoCommerce.Domain.Order.Services
{
    public interface IWorkflowService
    {
        WorkflowModel[] GetByIds(string[] workflowIds);
        void SaveChanges(WorkflowModel[] workflows);
        void Update(WorkflowModel workflow);
        void Delete(string[] ids);
    }
}

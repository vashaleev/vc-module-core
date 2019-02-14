using VirtoCommerce.Domain.Commerce.Model.Search;
using VirtoCommerce.Domain.Order.Model;
using VirtoCommerce.Domain.Order.Model.Search;

namespace VirtoCommerce.Domain.Order.Services
{
    public interface IWorkflowSearchService
    {
        GenericSearchResult<WorkflowModel> SearchWorkflows(WorkflowSearchCriteria criteria);
    }
}

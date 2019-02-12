using VirtoCommerce.Domain.Commerce.Model.Search;

namespace VirtoCommerce.Domain.Order.Model.Search
{
    public class WorkflowSearchCriteria : SearchCriteriaBase
    {
        public string Name { get; set; }

        public string MemberId { get; set; }

        public bool? IsActive { get; set; }
    }
}

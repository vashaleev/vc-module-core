using VirtoCommerce.Platform.Core.Common;

namespace VirtoCommerce.Domain.Order.Model
{
    public class WorkflowModel : AuditableEntity
    {
        public string Workflow { get; set; }

        public string Name { get; set; }

        public string MemberId { get; set; }

        public bool IsActive { get; set; }

        public bool IsDeleted { get; set; }
    }
}

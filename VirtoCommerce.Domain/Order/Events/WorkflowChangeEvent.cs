using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VirtoCommerce.Domain.Common.Events;
using VirtoCommerce.Domain.Order.Model;

namespace VirtoCommerce.Domain.Order.Events
{
    public class WorkflowChangeEvent : GenericChangedEntryEvent<WorkflowModel>
    {
        public WorkflowChangeEvent(IEnumerable<GenericChangedEntry<WorkflowModel>> changedEntries)
           : base(changedEntries)
        {

        }
    }
}

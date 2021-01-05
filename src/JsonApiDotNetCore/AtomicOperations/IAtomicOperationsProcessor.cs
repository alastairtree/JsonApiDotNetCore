using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JsonApiDotNetCore.Resources;

namespace JsonApiDotNetCore.AtomicOperations
{
    /// <summary>
    /// Atomically processes a request that contains a list of operations.
    /// </summary>
    public interface IAtomicOperationsProcessor
    {
        Task<IList<IIdentifiable>> ProcessAsync(IList<OperationContainer> operations, CancellationToken cancellationToken);
    }
}

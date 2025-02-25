//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Handlers
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Documents;

    /// <summary>
    /// Handler which selects the piepline for the requested resource operation
    /// </summary>
    internal class RouterHandler : RequestHandler
    {
        private readonly RequestHandler documentFeedHandler;
        private readonly RequestHandler pointOperationHandler;

        public RouterHandler(
            RequestHandler documentFeedHandler, 
            RequestHandler pointOperationHandler)
        {
            if (documentFeedHandler == null)
            {
                throw new ArgumentNullException(nameof(documentFeedHandler));
            }

            if (pointOperationHandler == null)
            {
                throw new ArgumentNullException(nameof(pointOperationHandler));
            }

            this.documentFeedHandler = documentFeedHandler;
            this.pointOperationHandler = pointOperationHandler;
        }

        public override Task<ResponseMessage> SendAsync(
            RequestMessage request, 
            CancellationToken cancellationToken)
        {
            RequestHandler targetHandler = null;
            if (request.IsPartitionedFeedOperation)
            {
                targetHandler = documentFeedHandler;
            }
            else
            {
                targetHandler = pointOperationHandler;
            }

            return targetHandler.SendAsync(request, cancellationToken);
        }
    }
}
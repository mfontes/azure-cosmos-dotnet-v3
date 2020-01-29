﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------
namespace Microsoft.Azure.Cosmos.Query.Core.ExecutionComponent.SkipTake
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.CosmosElements;
    using Microsoft.Azure.Cosmos.Json;
    using Microsoft.Azure.Cosmos.Query.Core.ContinuationTokens;
    using Microsoft.Azure.Cosmos.Query.Core.ExecutionContext;
    using Microsoft.Azure.Cosmos.Query.Core.Monads;
    using Microsoft.Azure.Cosmos.Query.Core.QueryClient;

    internal abstract partial class SkipDocumentQueryExecutionComponent : DocumentQueryExecutionComponentBase
    {
        private int skipCount;

        protected SkipDocumentQueryExecutionComponent(IDocumentQueryExecutionComponent source, long skipCount)
            : base(source)
        {
            if (skipCount > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(skipCount));
            }

            this.skipCount = (int)skipCount;
        }

        public static Task<TryCatch<IDocumentQueryExecutionComponent>> TryCreateAsync(
            ExecutionEnvironment executionEnvironment,
            int offsetCount,
            RequestContinuationToken continuationToken,
            Func<RequestContinuationToken, Task<TryCatch<IDocumentQueryExecutionComponent>>> tryCreateSourceAsync)
        {
            Task<TryCatch<IDocumentQueryExecutionComponent>> tryCreate;
            switch (executionEnvironment)
            {
                case ExecutionEnvironment.Client:
                    tryCreate = ClientSkipDocumentQueryExecutionComponent.TryCreateAsync(
                        offsetCount,
                        continuationToken,
                        tryCreateSourceAsync);
                    break;

                case ExecutionEnvironment.Compute:
                    tryCreate = ComputeSkipDocumentQueryExecutionComponent.TryCreateComputeAsync(
                        offsetCount,
                        continuationToken,
                        tryCreateSourceAsync);
                    break;

                default:
                    throw new ArgumentException($"Unknown {nameof(ExecutionEnvironment)}: {executionEnvironment}");
            }

            return tryCreate;
        }

        public override bool IsDone
        {
            get
            {
                return this.Source.IsDone;
            }
        }
    }
}
﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------
namespace Microsoft.Azure.Cosmos.Query.Core.ExecutionComponent.Distinct
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.CosmosElements;
    using Microsoft.Azure.Cosmos.Json;
    using Microsoft.Azure.Cosmos.Query.Core.ContinuationTokens;
    using Microsoft.Azure.Cosmos.Query.Core.Exceptions;
    using Microsoft.Azure.Cosmos.Query.Core.Monads;
    using Microsoft.Azure.Cosmos.Query.Core.QueryClient;

    internal abstract partial class DistinctDocumentQueryExecutionComponent : DocumentQueryExecutionComponentBase
    {
        /// <summary>
        /// Compute implementation of DISTINCT.
        /// Here we never serialize the continuation token, but you can always retrieve it on demand with TryGetContinuationToken.
        /// </summary>
        private sealed class ComputeDistinctDocumentQueryExecutionComponent : DistinctDocumentQueryExecutionComponent
        {
            private static readonly string UseTryGetContinuationTokenMessage = $"Use {nameof(ComputeDistinctDocumentQueryExecutionComponent.TryGetContinuationToken)}";

            private ComputeDistinctDocumentQueryExecutionComponent(
                DistinctQueryType distinctQueryType,
                DistinctMap distinctMap,
                IDocumentQueryExecutionComponent source)
                : base(distinctMap, source)
            {
            }

            public static async Task<TryCatch<IDocumentQueryExecutionComponent>> TryCreateAsync(
                RequestContinuationToken requestContinuation,
                Func<RequestContinuationToken, Task<TryCatch<IDocumentQueryExecutionComponent>>> tryCreateSourceAsync,
                DistinctQueryType distinctQueryType)
            {
                if (requestContinuation == null)
                {
                    throw new ArgumentNullException(nameof(requestContinuation));
                }

                if (!(requestContinuation is CosmosElementRequestContinuationToken cosmosElementRequestContinuationToken))
                {
                    throw new ArgumentException($"Unknown {nameof(RequestContinuationToken)} type: {requestContinuation.GetType()}");
                }

                if (tryCreateSourceAsync == null)
                {
                    throw new ArgumentNullException(nameof(tryCreateSourceAsync));
                }

                DistinctContinuationToken distinctContinuationToken;
                if (requestContinuation != null)
                {
                    if (!DistinctContinuationToken.TryParse(cosmosElementRequestContinuationToken.Value, out distinctContinuationToken))
                    {
                        return TryCatch<IDocumentQueryExecutionComponent>.FromException(
                            new MalformedContinuationTokenException($"Invalid {nameof(DistinctContinuationToken)}: {cosmosElementRequestContinuationToken.Value}"));
                    }
                }
                else
                {
                    distinctContinuationToken = new DistinctContinuationToken(sourceToken: null, distinctMapToken: null);
                }

                TryCatch<DistinctMap> tryCreateDistinctMap = DistinctMap.TryCreate(
                    distinctQueryType,
                    RequestContinuationToken.Create(distinctContinuationToken.DistinctMapToken));
                if (!tryCreateDistinctMap.Succeeded)
                {
                    return TryCatch<IDocumentQueryExecutionComponent>.FromException(tryCreateDistinctMap.Exception);
                }

                TryCatch<IDocumentQueryExecutionComponent> tryCreateSource = await tryCreateSourceAsync(
                    RequestContinuationToken.Create(distinctContinuationToken.SourceToken));
                if (!tryCreateSource.Succeeded)
                {
                    return TryCatch<IDocumentQueryExecutionComponent>.FromException(tryCreateSource.Exception);
                }

                return TryCatch<IDocumentQueryExecutionComponent>.FromResult(
                    new ComputeDistinctDocumentQueryExecutionComponent(
                        distinctQueryType,
                        tryCreateDistinctMap.Result,
                        tryCreateSource.Result));
            }

            /// <summary>
            /// Drains a page of results returning only distinct elements.
            /// </summary>
            /// <param name="maxElements">The maximum number of items to drain.</param>
            /// <param name="cancellationToken">The cancellation token.</param>
            /// <returns>A page of distinct results.</returns>
            public override async Task<QueryResponseCore> DrainAsync(int maxElements, CancellationToken cancellationToken)
            {
                List<CosmosElement> distinctResults = new List<CosmosElement>();
                QueryResponseCore sourceResponse = await base.DrainAsync(maxElements, cancellationToken);

                if (!sourceResponse.IsSuccess)
                {
                    return sourceResponse;
                }

                foreach (CosmosElement document in sourceResponse.CosmosElements)
                {
                    if (this.distinctMap.Add(document, out UInt128 hash))
                    {
                        distinctResults.Add(document);
                    }
                }

                return QueryResponseCore.CreateSuccess(
                        result: distinctResults,
                        continuationToken: null,
                        disallowContinuationTokenMessage: ComputeDistinctDocumentQueryExecutionComponent.UseTryGetContinuationTokenMessage,
                        activityId: sourceResponse.ActivityId,
                        requestCharge: sourceResponse.RequestCharge,
                        diagnostics: sourceResponse.Diagnostics,
                        responseLengthBytes: sourceResponse.ResponseLengthBytes);
            }

            public override void SerializeState(IJsonWriter jsonWriter)
            {
                if (jsonWriter == null)
                {
                    throw new ArgumentNullException(nameof(jsonWriter));
                }

                if (!this.IsDone)
                {
                    jsonWriter.WriteObjectStart();
                    jsonWriter.WriteFieldName(DistinctDocumentQueryExecutionComponent.SourceTokenName);
                    this.Source.SerializeState(jsonWriter);
                    jsonWriter.WriteFieldName(DistinctDocumentQueryExecutionComponent.DistinctMapTokenName);
                    this.distinctMap.SerializeState(jsonWriter);
                    jsonWriter.WriteObjectEnd();
                }
            }

            private readonly struct DistinctContinuationToken
            {
                public DistinctContinuationToken(CosmosElement sourceToken, CosmosElement distinctMapToken)
                {
                    this.SourceToken = sourceToken;
                    this.DistinctMapToken = distinctMapToken;
                }

                public CosmosElement SourceToken { get; }

                public CosmosElement DistinctMapToken { get; }

                public static bool TryParse(
                    CosmosElement requestContinuationToken,
                    out DistinctContinuationToken distinctContinuationToken)
                {
                    if (requestContinuationToken == null)
                    {
                        distinctContinuationToken = default;
                        return false;
                    }

                    if (!(requestContinuationToken is CosmosObject rawObject))
                    {
                        distinctContinuationToken = default;
                        return false;
                    }

                    if (!rawObject.TryGetValue(SourceTokenName, out CosmosElement sourceToken))
                    {
                        distinctContinuationToken = default;
                        return false;
                    }

                    if (!rawObject.TryGetValue(DistinctMapTokenName, out CosmosElement distinctMapToken))
                    {
                        distinctContinuationToken = default;
                        return false;
                    }

                    distinctContinuationToken = new DistinctContinuationToken(sourceToken: sourceToken, distinctMapToken: distinctMapToken);
                    return true;
                }
            }
        }
    }
}

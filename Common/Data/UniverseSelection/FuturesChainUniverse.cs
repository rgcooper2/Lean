/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
*/

using System;
using System.Collections.Generic;
using System.Linq;
using QuantConnect.Data.Market;
using QuantConnect.Interfaces;
using QuantConnect.Securities;
using QuantConnect.Securities.Future;

namespace QuantConnect.Data.UniverseSelection
{
    /// <summary>
    /// Defines a universe for a single futures chain
    /// </summary>
    public class FuturesChainUniverse : Universe
    {
        private readonly UniverseSettings _universeSettings;
        private DateTime _cacheDate;

        /// <summary>
        /// Initializes a new instance of the <see cref="FuturesChainUniverse"/> class
        /// </summary>
        /// <param name="future">The canonical future chain security</param>
        /// <param name="universeSettings">The universe settings to be used for new subscriptions</param>
        public FuturesChainUniverse(Future future,
            UniverseSettings universeSettings)
            : base(future.SubscriptionDataConfig)
        {
            Future = future;
            _universeSettings = universeSettings;
        }

        /// <summary>
        /// The canonical future chain security
        /// </summary>
        public Future Future { get; }

        /// <summary>
        /// Gets the settings used for subscriptons added for this universe
        /// </summary>
        public override UniverseSettings UniverseSettings
        {
            get { return _universeSettings; }
        }

        /// <summary>
        /// Performs universe selection using the data specified
        /// </summary>
        /// <param name="utcTime">The current utc time</param>
        /// <param name="data">The symbols to remain in the universe</param>
        /// <returns>The data that passes the filter</returns>
        public override IEnumerable<Symbol> SelectSymbols(DateTime utcTime, BaseDataCollection data)
        {
            var underlying = new Tick { Time = utcTime };

            // date change detection needs to be done in exchange time zone
            if (_cacheDate == data.Time.ConvertFromUtc(Future.Exchange.TimeZone).Date)
            {
                return Unchanged;
            }

            var availableContracts = data.Data.Select(x => x.Symbol);
            var results = Future.ContractFilter.Filter(new FutureFilterUniverse(availableContracts, underlying));

            // if results are not dynamic, we cache them and won't call filtering till the end of the day
            if (!results.IsDynamic)
            {
                _cacheDate = data.Time.ConvertFromUtc(Future.Exchange.TimeZone).Date;
            }

            return results;
        }

        /// <summary>
        /// Determines whether or not the specified security can be removed from
        /// this universe. This is useful to prevent securities from being taken
        /// out of a universe before the algorithm has had enough time to make
        /// decisions on the security
        /// </summary>
        /// <param name="utcTime">The current utc time</param>
        /// <param name="security">The security to check if its ok to remove</param>
        /// <returns>True if we can remove the security, false otherwise</returns>
        public override bool CanRemoveMember(DateTime utcTime, Security security)
        {
            // can always remove securities after dispose requested
            if (DisposeRequested)
            {
                return true;
            }

            // if we haven't begun receiving data for this security then it's safe to remove
            var lastData = security.Cache.GetData();
            if (lastData == null)
            {
                return true;
            }

            // only remove members on day changes, this prevents us from needing to
            // fast forward contracts continuously as price moves and out filtered
            // contracts change thoughout the day
            var localTime = utcTime.ConvertFromUtc(security.Exchange.TimeZone);
            if (localTime.Date != lastData.Time.Date)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets the subscription requests to be added for the specified security
        /// </summary>
        /// <param name="security">The security to get subscriptions for</param>
        /// <param name="currentTimeUtc">The current time in utc. This is the frontier time of the algorithm</param>
        /// <param name="maximumEndTimeUtc">The max end time</param>
        /// <param name="subscriptionService">Instance which implements <see cref="ISubscriptionDataConfigService"/> interface</param>
        /// <returns>All subscriptions required by this security</returns>
        public override IEnumerable<SubscriptionRequest> GetSubscriptionRequests(Security security, DateTime currentTimeUtc, DateTime maximumEndTimeUtc,
            ISubscriptionDataConfigService subscriptionService)
        {
            if (Future.Symbol.Underlying == security.Symbol)
            {
                Future.Underlying = security;
            }

            return base.GetSubscriptionRequests(security, currentTimeUtc, maximumEndTimeUtc, subscriptionService);
        }
    }
}

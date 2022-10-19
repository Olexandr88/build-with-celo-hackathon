﻿using System.Numerics;

using Nomis.Blockchain.Abstractions.Calculators;
using Nomis.Celoscan.Extensions;
using Nomis.Celoscan.Interfaces.Models;
using Nomis.Utils.Extensions;

namespace Nomis.Celoscan.Calculators
{
    /// <summary>
    /// Celo wallet stats calculator.
    /// </summary>
    internal sealed class CeloStatCalculator :
        IStatCalculator<CeloWalletStats>
    {
        private readonly string _address;
        private readonly decimal _balance;
        private readonly IEnumerable<CeloscanAccountNormalTransaction> _transactions;
        private readonly IEnumerable<CeloscanAccountInternalTransaction> _internalTransactions;
        private readonly IEnumerable<ICeloscanAccountNftTokenEvent> _tokenTransfers;
        private readonly IEnumerable<CeloscanAccountERC20TokenEvent> _ecr20TokenTransfers;

        public CeloStatCalculator(
            string address,
            decimal balance,
            IEnumerable<CeloscanAccountNormalTransaction> transactions,
            IEnumerable<CeloscanAccountInternalTransaction> internalTransactions,
            IEnumerable<ICeloscanAccountNftTokenEvent> tokenTransfers,
            IEnumerable<CeloscanAccountERC20TokenEvent> ecr20TokenTransfers)
        {
            _address = address;
            _balance = balance;
            _transactions = transactions;
            _internalTransactions = internalTransactions;
            _tokenTransfers = tokenTransfers;
            _ecr20TokenTransfers = ecr20TokenTransfers;
        }

        public CeloWalletStats GetStats()
        {
            if (!_transactions.Any())
            {
                return new()
                {
                    NoData = true
                };
            }

            var intervals = IStatCalculator<CeloWalletStats>
                .GetTransactionsIntervals(_transactions.Select(x => x.TimeStamp!.ToDateTime())).ToList();
            if (!intervals.Any())
            {
                return new()
                {
                    NoData = true
                };
            }

            var monthAgo = DateTime.Now.AddMonths(-1);
            var yearAgo = DateTime.Now.AddYears(-1);

            var soldTokens = _tokenTransfers.Where(x => x.From?.Equals(_address, StringComparison.InvariantCultureIgnoreCase) == true).ToList();
            var soldSum = IStatCalculator<CeloWalletStats>
                .GetTokensSum(soldTokens.Select(x => x.Hash!), _internalTransactions.Select(x => (x.Hash!, ulong.TryParse(x.Value, out var amount) ? amount : new BigInteger(0))));

            var soldTokensIds = soldTokens.Select(x => x.GetTokenUid());
            var buyTokens = _tokenTransfers.Where(x => x.To?.Equals(_address, StringComparison.InvariantCultureIgnoreCase) == true && soldTokensIds.Contains(x.GetTokenUid()));
            var buySum = IStatCalculator<CeloWalletStats>
                .GetTokensSum(buyTokens.Select(x => x.Hash!), _internalTransactions.Select(x => (x.Hash!, ulong.TryParse(x.Value, out var amount) ? amount : new BigInteger(0))));

            var buyNotSoldTokens = _tokenTransfers.Where(x => x.To?.Equals(_address, StringComparison.InvariantCultureIgnoreCase) == true && !soldTokensIds.Contains(x.GetTokenUid()));
            var buyNotSoldSum = IStatCalculator<CeloWalletStats>
                .GetTokensSum(buyNotSoldTokens.Select(x => x.Hash!), _internalTransactions.Select(x => (x.Hash!, ulong.TryParse(x.Value, out var amount) ? amount : new BigInteger(0))));

            var holdingTokens = _tokenTransfers.Count() - soldTokens.Count;
            var nftWorth = buySum == 0 ? 0 : (decimal)soldSum / (decimal)buySum * (decimal)buyNotSoldSum;
            var contractsCreated = _transactions.Count(x => !string.IsNullOrWhiteSpace(x.ContractAddress));
            var totalTokens = _ecr20TokenTransfers.Select(x => x.TokenSymbol).Distinct();

            return new()
            {
                Balance = _balance.ToCelo(),
                WalletAge = IStatCalculator<CeloWalletStats>
                    .GetWalletAge(_transactions.Select(x => x.TimeStamp)),
                TotalTransactions = _transactions.Count(),
                MinTransactionTime = intervals.Min(),
                MaxTransactionTime = intervals.Max(),
                AverageTransactionTime = intervals.Average(),
                WalletTurnover = _transactions.Sum(x =>
                {
                    if (ulong.TryParse(x.Value, out var value))
                    {
                        return (decimal)value;
                    }

                    return (decimal)0;
                }).ToCelo(),
                LastMonthTransactions = _transactions.Count(x => x.TimeStamp!.ToDateTime() > monthAgo),
                TimeFromLastTransaction = (int)((DateTime.UtcNow - _transactions.OrderBy(x => x.TimeStamp).Last().TimeStamp!.ToDateTime()).TotalDays / 30),
                NftHolding = holdingTokens,
                NftTrading = (soldSum - buySum).ToCelo(),
                NftWorth = nftWorth.ToCelo(),
                DeployedContracts = contractsCreated,
                TokensHolding = totalTokens.Count()
            };
        }
    }
}
using System;
using Akka.TestKit.Xunit2;
using Akkatecture.TestFixture.Extensions;
using Domain.Model.Account;
using Domain.Model.Account.Commands;
using Domain.Model.Account.Entities;
using Domain.Model.Account.Events;
using Domain.Model.Account.ValueObjects;
using Xunit;

namespace Domain.Tests
{
    public class AccountTests : TestKit
    {
        [Fact]
        public void WhenOpenAccountCommand_ShouldEmitAccountOpen()
        {
            var accountId = AccountId.New;
            var money = new Money(50.1m);

            this.FixtureFor<Account, AccountId>(accountId)
                .GivenNothing()
                .When(new OpenNewAccountCommand(accountId, money))
                .ThenExpect<AccountOpenedEvent>(x => x.OpeningBalance == money);
        }
        
        [Fact]
        public void GivenAccountIsOpened_WhenTransferIsCommanded_ShouldEmitAccountOpen()
        {
            var accountId = AccountId.New;
            var receiverAccountId = AccountId.New;
            var openingBalance = new Money(20.3m);
            var transferAmount = new Money(10.98m);
            var transactionId = TransactionId.New;
            var transaction = new Transaction(transactionId, accountId, receiverAccountId, transferAmount);

            this.FixtureFor<Account, AccountId>(accountId)
                .Given(new AccountOpenedEvent(openingBalance))
                .When(new TransferMoneyCommand(accountId, transaction))
                .ThenExpect<MoneySentEvent>(x => x.Transaction == transaction)
                .ThenExpect<FeesDeductedEvent>(x => x.Amount.Value == 0.25m);
        }
    }
}
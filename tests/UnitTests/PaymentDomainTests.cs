using PaymentGateway.Payments.Application;
using PaymentGateway.Payments.Domain;
using Xunit;

namespace PaymentGateway.Tests.Unit;

public sealed class PaymentDomainTests
{
    [Fact]
    public void Unknown_CannotBeTreatedAsFailedWithoutVerifiedEvidence()
    {
        Assert.False(PaymentTransitions.Allows(PaymentState.Pending, PaymentState.Succeeded));
        Assert.True(PaymentTransitions.Allows(PaymentState.Processing, PaymentState.Unknown));
        Assert.True(PaymentTransitions.Allows(PaymentState.Unknown, PaymentState.Succeeded));
        Assert.False(PaymentTransitions.Allows(PaymentState.Succeeded, PaymentState.Failed));
    }

    [Fact]
    public async Task CreatePayment_RejectsInvalidAmountBeforeStore()
    {
        var fake = new NoOpStore();
        var service = new PaymentService(fake);
        var command = new CreatePaymentCommand(Guid.NewGuid(), "same-key-123", "order-101",
            0, "VND", "MOCK");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(command, CancellationToken.None));
        Assert.False(fake.Called);
    }

    [Fact]
    public async Task CreatePayment_RejectsNonMockMethodsInFoundation()
    {
        var fake = new NoOpStore();
        var service = new PaymentService(fake);
        var command = new CreatePaymentCommand(Guid.NewGuid(), "same-key-123", "order-101",
            100_000, "VND", "VIETQR");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(command, CancellationToken.None));
        Assert.False(fake.Called);
    }

    private sealed class NoOpStore : IPaymentStore
    {
        public bool Called { get; private set; }
        public Task<CreationResult> CreateOrGetAsync(CreatePaymentCommand c, byte[] hash, CancellationToken ct)
        {
            Called = true;
            throw new InvalidOperationException("Not called in validation tests.");
        }
        public Task<PaymentSnapshot?> GetAsync(Guid merchantId, Guid paymentId, CancellationToken ct)
            => Task.FromResult<PaymentSnapshot?>(null);
    }
}

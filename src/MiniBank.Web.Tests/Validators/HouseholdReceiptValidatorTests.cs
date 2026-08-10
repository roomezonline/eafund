using FluentAssertions;
using MiniBank.Web.Models;
using MiniBank.Web.Validators;
using Xunit;

namespace MiniBank.Web.Tests.Validators
{
    public class HouseholdReceiptValidatorTests
    {
        private readonly HouseholdReceiptValidator _validator;

        public HouseholdReceiptValidatorTests()
        {
            _validator = new HouseholdReceiptValidator();
        }

        [Fact]
        public void Should_Pass_When_Valid_Receipt()
        {
            // Arrange
            var receipt = new HouseholdReceipt
            {
                TargetHouseholdId = 1,
                AmountTotal = 1000000,
                PayerType = 1,
                PayChannel = 1,
                OccurredAt = DateTime.UtcNow,
                CreatedBy = "test"
            };

            // Act
            var result = _validator.Validate(receipt);

            // Assert
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void Should_Fail_When_TargetHouseholdId_Is_Zero()
        {
            // Arrange
            var receipt = new HouseholdReceipt
            {
                TargetHouseholdId = 0,
                AmountTotal = 1000000,
                PayerType = 1,
                PayChannel = 1
            };

            // Act
            var result = _validator.Validate(receipt);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(HouseholdReceipt.TargetHouseholdId));
        }

        [Fact]
        public void Should_Fail_When_AmountTotal_Is_Zero()
        {
            // Arrange
            var receipt = new HouseholdReceipt
            {
                TargetHouseholdId = 1,
                AmountTotal = 0,
                PayerType = 1,
                PayChannel = 1
            };

            // Act
            var result = _validator.Validate(receipt);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(HouseholdReceipt.AmountTotal));
        }

        [Fact]
        public void Should_Fail_When_AmountTotal_Exceeds_Limit()
        {
            // Arrange
            var receipt = new HouseholdReceipt
            {
                TargetHouseholdId = 1,
                AmountTotal = 2000000000, // بیشتر از یک میلیارد
                PayerType = 1,
                PayChannel = 1
            };

            // Act
            var result = _validator.Validate(receipt);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(HouseholdReceipt.AmountTotal));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(4)]
        public void Should_Fail_When_PayerType_Is_Invalid(byte payerType)
        {
            // Arrange
            var receipt = new HouseholdReceipt
            {
                TargetHouseholdId = 1,
                AmountTotal = 1000000,
                PayerType = payerType,
                PayChannel = 1
            };

            // Act
            var result = _validator.Validate(receipt);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(HouseholdReceipt.PayerType));
        }

        [Fact]
        public void Should_Require_PayerMemberId_When_PayerType_Is_Member()
        {
            // Arrange
            var receipt = new HouseholdReceipt
            {
                TargetHouseholdId = 1,
                AmountTotal = 1000000,
                PayerType = 2, // Member
                PayerMemberId = null,
                PayChannel = 1
            };

            // Act
            var result = _validator.Validate(receipt);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(HouseholdReceipt.PayerMemberId));
        }

        [Fact]
        public void Should_Require_ExternalPayer_Details_When_PayerType_Is_External()
        {
            // Arrange
            var receipt = new HouseholdReceipt
            {
                TargetHouseholdId = 1,
                AmountTotal = 1000000,
                PayerType = 3, // External
                PayerExternalFullName = "", // Empty
                PayChannel = 1
            };

            // Act
            var result = _validator.Validate(receipt);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(HouseholdReceipt.PayerExternalFullName));
        }

        [Theory]
        [InlineData("123456789")] // 9 digits
        [InlineData("12345678901")] // 11 digits
        [InlineData("abc1234567")] // non-numeric
        public void Should_Fail_When_NationalId_Is_Invalid(string nationalId)
        {
            // Arrange
            var receipt = new HouseholdReceipt
            {
                TargetHouseholdId = 1,
                AmountTotal = 1000000,
                PayerType = 3,
                PayerExternalFullName = "Test User",
                PayerExternalNationalId = nationalId,
                PayChannel = 1
            };

            // Act
            var result = _validator.Validate(receipt);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(HouseholdReceipt.PayerExternalNationalId));
        }

        [Theory]
        [InlineData("0912345678")] // 10 digits
        [InlineData("9123456789")] // doesn't start with 09
        [InlineData("091234567890")] // 12 digits
        public void Should_Fail_When_Mobile_Is_Invalid(string mobile)
        {
            // Arrange
            var receipt = new HouseholdReceipt
            {
                TargetHouseholdId = 1,
                AmountTotal = 1000000,
                PayerType = 3,
                PayerExternalFullName = "Test User",
                PayerExternalMobile = mobile,
                PayChannel = 1
            };

            // Act
            var result = _validator.Validate(receipt);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(HouseholdReceipt.PayerExternalMobile));
        }
    }
}

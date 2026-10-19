using System.ComponentModel.DataAnnotations;
using System.Globalization;
using StockReplenishment.Contracts.Requests;

namespace StockReplenishment.Api.Tests.Api;

[TestFixture]
[SetCulture("sv-SE")]
public class CultureIndependenceTests
{
    [TestCase(0.5, true)]
    [TestCase(0.0001, false)]
    public void RequestLineInput_QuantityRange_ValidatesUnderCommaDecimalCulture(double quantity, bool expectedValid)
    {
        Assert.That(CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator, Is.EqualTo(","));
        var input = new RequestLineInput { ArticleNumber = "100-2045", RequestedQuantity = (decimal)quantity };

        var isValid = Validator.TryValidateObject(input, new ValidationContext(input), [], validateAllProperties: true);

        Assert.That(isValid, Is.EqualTo(expectedValid));
    }

    [Test]
    public void FulfilledLineInput_QuantityRange_ValidatesUnderCommaDecimalCulture()
    {
        var input = new FulfilledLineInput { LineId = 1, FulfilledQuantity = 2.5m };

        Assert.That(Validator.TryValidateObject(input, new ValidationContext(input), [], validateAllProperties: true), Is.True);
    }
}

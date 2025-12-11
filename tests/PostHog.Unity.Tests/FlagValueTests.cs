using PostHogUnity;

namespace PostHogUnity.Tests;

public class FlagValueTests
{
    public class TheFromObjectMethod
    {
        [Fact]
        public void WithNull_ReturnsNoValue()
        {
            var value = FlagValue.FromObject(null);

            Assert.False(value.HasValue);
            Assert.False(value.IsBool);
            Assert.False(value.IsString);
        }

        [Fact]
        public void WithTrue_ReturnsBoolValue()
        {
            var value = FlagValue.FromObject(true);

            Assert.True(value.HasValue);
            Assert.True(value.IsBool);
            Assert.False(value.IsString);
            Assert.True(value.BoolValue);
        }

        [Fact]
        public void WithFalse_ReturnsBoolValue()
        {
            var value = FlagValue.FromObject(false);

            Assert.True(value.HasValue);
            Assert.True(value.IsBool);
            Assert.False(value.BoolValue);
        }

        [Fact]
        public void WithString_ReturnsStringValue()
        {
            var value = FlagValue.FromObject("variant-a");

            Assert.True(value.HasValue);
            Assert.False(value.IsBool);
            Assert.True(value.IsString);
            Assert.Equal("variant-a", value.StringValue);
        }

        [Fact]
        public void WithNullString_ReturnsNoValue()
        {
            var value = FlagValue.FromObject((string)null);

            Assert.False(value.HasValue);
        }

        [Fact]
        public void WithOtherType_ReturnsNoValue()
        {
            var value = FlagValue.FromObject(42);

            Assert.False(value.HasValue);
        }
    }

    public class TheHasValueProperty
    {
        [Fact]
        public void WithBoolValue_ReturnsTrue()
        {
            var value = FlagValue.FromObject(true);

            Assert.True(value.HasValue);
        }

        [Fact]
        public void WithStringValue_ReturnsTrue()
        {
            var value = FlagValue.FromObject("test");

            Assert.True(value.HasValue);
        }

        [Fact]
        public void WithDefault_ReturnsFalse()
        {
            var value = default(FlagValue);

            Assert.False(value.HasValue);
        }
    }

    public class TheIsBoolProperty
    {
        [Fact]
        public void WithBoolValue_ReturnsTrue()
        {
            var value = FlagValue.FromObject(true);

            Assert.True(value.IsBool);
        }

        [Fact]
        public void WithStringValue_ReturnsFalse()
        {
            var value = FlagValue.FromObject("test");

            Assert.False(value.IsBool);
        }

        [Fact]
        public void WithDefault_ReturnsFalse()
        {
            var value = default(FlagValue);

            Assert.False(value.IsBool);
        }
    }

    public class TheIsStringProperty
    {
        [Fact]
        public void WithStringValue_ReturnsTrue()
        {
            var value = FlagValue.FromObject("variant");

            Assert.True(value.IsString);
        }

        [Fact]
        public void WithBoolValue_ReturnsFalse()
        {
            var value = FlagValue.FromObject(true);

            Assert.False(value.IsString);
        }

        [Fact]
        public void WithDefault_ReturnsFalse()
        {
            var value = default(FlagValue);

            Assert.False(value.IsString);
        }
    }

    public class TheBoolValueProperty
    {
        [Fact]
        public void WithTrue_ReturnsTrue()
        {
            var value = FlagValue.FromObject(true);

            Assert.True(value.BoolValue);
        }

        [Fact]
        public void WithFalse_ReturnsFalse()
        {
            var value = FlagValue.FromObject(false);

            Assert.False(value.BoolValue);
        }

        [Fact]
        public void WithStringValue_ReturnsFalse()
        {
            // When created from string, BoolValue should be false (default)
            var value = FlagValue.FromObject("test");

            Assert.False(value.BoolValue);
        }
    }

    public class TheStringValueProperty
    {
        [Fact]
        public void WithString_ReturnsString()
        {
            var value = FlagValue.FromObject("my-variant");

            Assert.Equal("my-variant", value.StringValue);
        }

        [Fact]
        public void WithBoolValue_ReturnsNull()
        {
            var value = FlagValue.FromObject(true);

            Assert.Null(value.StringValue);
        }

        [Fact]
        public void WithEmptyString_ReturnsEmptyString()
        {
            var value = FlagValue.FromObject("");

            Assert.Equal("", value.StringValue);
        }
    }

    public class TheIsEnabledProperty
    {
        [Fact]
        public void WithNoValue_ReturnsFalse()
        {
            var value = default(FlagValue);

            Assert.False(value.IsEnabled);
        }

        [Fact]
        public void WithTrue_ReturnsTrue()
        {
            var value = FlagValue.FromObject(true);

            Assert.True(value.IsEnabled);
        }

        [Fact]
        public void WithFalse_ReturnsFalse()
        {
            var value = FlagValue.FromObject(false);

            Assert.False(value.IsEnabled);
        }

        [Fact]
        public void WithNonEmptyString_ReturnsTrue()
        {
            var value = FlagValue.FromObject("variant");

            Assert.True(value.IsEnabled);
        }

        [Fact]
        public void WithEmptyString_ReturnsFalse()
        {
            var value = FlagValue.FromObject("");

            Assert.False(value.IsEnabled);
        }
    }

    public class TheImplicitBoolOperator
    {
        [Fact]
        public void WithTrue_ReturnsTrue()
        {
            FlagValue value = FlagValue.FromObject(true);
            bool result = value;

            Assert.True(result);
        }

        [Fact]
        public void WithFalse_ReturnsFalse()
        {
            FlagValue value = FlagValue.FromObject(false);
            bool result = value;

            Assert.False(result);
        }

        [Fact]
        public void WithNonEmptyString_ReturnsTrue()
        {
            FlagValue value = FlagValue.FromObject("test");
            bool result = value;

            Assert.True(result);
        }

        [Fact]
        public void WithDefault_ReturnsFalse()
        {
            FlagValue value = default;
            bool result = value;

            Assert.False(result);
        }

        [Fact]
        public void InIfStatement_WorksCorrectly()
        {
            var enabled = FlagValue.FromObject(true);
            var disabled = FlagValue.FromObject(false);

            var enabledResult = false;
            var disabledResult = true;

            if (enabled)
            {
                enabledResult = true;
            }

            if (disabled)
            {
                disabledResult = false;
            }

            Assert.True(enabledResult);
            Assert.True(disabledResult);
        }
    }

    public class TheToStringMethod
    {
        [Fact]
        public void WithNoValue_ReturnsNull()
        {
            var value = default(FlagValue);

            Assert.Equal("null", value.ToString());
        }

        [Fact]
        public void WithTrue_ReturnsTrue()
        {
            var value = FlagValue.FromObject(true);

            Assert.Equal("True", value.ToString());
        }

        [Fact]
        public void WithFalse_ReturnsFalse()
        {
            var value = FlagValue.FromObject(false);

            Assert.Equal("False", value.ToString());
        }

        [Fact]
        public void WithString_ReturnsString()
        {
            var value = FlagValue.FromObject("variant-a");

            Assert.Equal("variant-a", value.ToString());
        }
    }
}

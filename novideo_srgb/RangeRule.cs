using System;
using System.Globalization;
using System.Linq;
using System.Windows.Controls;

namespace novideo_srgb
{
    public class RangeRule : ValidationRule
    {
        public int Min { get; set; }
        public int Max { get; set; }

        public override ValidationResult Validate(object valueObj, CultureInfo cultureInfo)
        {
            if (valueObj == null) return null;

            var valueString = (string)valueObj;
            if (valueString.EndsWith("."))
            {
                return new ValidationResult(false, "Input must not end in '.'");
            }

            double value = 0;
            try
            {
                if (valueString.Length > 0)
                    value = Double.Parse(valueString.Replace(',', 'a'), CultureInfo.InvariantCulture);
            }
            catch (Exception e)
            {
                return new ValidationResult(false, String.Format("Illegal characters or {0}", e.Message));
            }

            if (value < Min || value > Max)
            {
                return new ValidationResult(false,
                    String.Format("Value must be between {0} and {1}", Min, Max));
            }

            return ValidationResult.ValidResult;
        }
    }
}
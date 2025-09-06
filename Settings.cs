using System.Collections.Generic;
using ExileCore2.Shared.Attributes;
using ExileCore2.Shared.Interfaces;
using ExileCore2.Shared.Nodes;

namespace CurrencyExchangeAutoFill
{
    public class Settings : ISettings
    {
        public ToggleNode Enable { get; set; } = new(true);
    }
}
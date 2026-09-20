using System;
using System.Collections.Generic;
using System.Linq;
using RejiDisplay.Models;

namespace RejiDisplay.Services
{
    public class VenuePresetService
    {
        private readonly List<VenuePreset> _presets;

        public VenuePresetService()
        {
            _presets = new List<VenuePreset>
            {
                VenuePreset.NovaStarVX2000ProStandard,
                VenuePreset.FitToSignal
            };
        }

        public List<VenuePreset> GetPresets() => _presets.ToList();

        public VenuePreset? GetPresetByName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return _presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}

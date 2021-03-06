using Microsoft.TeamFoundation.Build.Client;
using System;
using System.Globalization;
using System.Windows.Data;

namespace TeamProjectManager.Modules.XamlBuild.BuildDefinitions
{
    public class TriggerTypeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var triggerType = (DefinitionTriggerType)value;
            switch (triggerType)
            {
                case DefinitionTriggerType.None:
                    return "Manual";
                case DefinitionTriggerType.ContinuousIntegration:
                    return "Continuous Integration";
                case DefinitionTriggerType.BatchedContinuousIntegration:
                    return "Rolling";
                case DefinitionTriggerType.GatedCheckIn:
                    return "Gated Check-in";
                case DefinitionTriggerType.BatchedGatedCheckIn:
                    return "Batched Gated Check-in";
                case DefinitionTriggerType.Schedule:
                    return "Schedule";
                case DefinitionTriggerType.ScheduleForced:
                    return "Schedule (Forced)";
                default:
                    return triggerType.ToString();
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
using Autofac;
using Autofac.Builder;
using System.Reflection;

namespace WhisperWriter.DI;

public static class AutofacExtensions {
	public static IRegistrationBuilder<TLimit, TActivatorData, TRegistrationStyle> InjectProtectedProperties<TLimit, TActivatorData, TRegistrationStyle> (
		this IRegistrationBuilder<TLimit, TActivatorData, TRegistrationStyle> registration
	) {
		return registration.OnActivated(e => {
			var props = e.Instance?.GetType()
				.GetProperties(BindingFlags.NonPublic | BindingFlags.Instance)
				.Where(p => p.GetCustomAttribute<InjectAttribute>() != null) ?? [];

			foreach (var prop in props) {
				var service = e.Context.Resolve(prop.PropertyType);
				prop.SetValue(e.Instance, service);
			}
		});
	}
}
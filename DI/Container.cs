using Autofac;
using System.Reflection;
using WhisperWriter.Utils.Interfaces;
using WhisperWriter.Views;

namespace WhisperWriter.DI;

public class Container {

	/// <summary>
	/// Singleton DI container instance.
	/// </summary>
	private static Container _instance = null!;

	public IContainer Provider { get; private set; }

	/// <summary>
	/// Singleton DI container instance getter.
	/// </summary>
	/// <returns></returns>
	public static Container GetInstance () {
		if (Container._instance == null) {
			Container._instance = new Container();
		}
		return Container._instance;
	}

	public Container () {
		// Registrace v kontejneru
		var builder = new ContainerBuilder();

		var allServices = Assembly.GetExecutingAssembly()
				.GetTypes()
				.Where(t => t.IsClass && !t.IsAbstract && typeof(IService).IsAssignableFrom(t));
		
		builder.RegisterTypes(allServices.Where(t => typeof(ITransient).IsAssignableFrom(t) && t != typeof(MainWindow)).ToArray())
			.PropertiesAutowired(PropertyWiringOptions.AllowCircularDependencies)
			.InjectProtectedProperties()
			.AsSelf()
			.AsImplementedInterfaces()
			.InstancePerDependency();

		builder.RegisterTypes(allServices.Where(t => typeof(ISingleton).IsAssignableFrom(t) && t != typeof(MainWindow)).ToArray())
			.PropertiesAutowired(PropertyWiringOptions.AllowCircularDependencies)
			.InjectProtectedProperties()
			.AsSelf()
			.AsImplementedInterfaces()
			.SingleInstance();

		builder.RegisterType<MainWindow>()
			.PropertiesAutowired(PropertyWiringOptions.AllowCircularDependencies)
			.InjectProtectedProperties()
			.AsSelf()
			.SingleInstance();

		this.Provider = builder.Build();
	}
}

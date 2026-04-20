using Autofac;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using WhisperWriter.Utils.Interfaces;

namespace WhisperWriter.Services;

public class DI {

	/// <summary>
	/// Singleton DI container instance.
	/// </summary>
	private static DI _instance = null!;

	public IContainer Container { get; private set; }

	/// <summary>
	/// Singleton DI container instance getter.
	/// </summary>
	/// <returns></returns>
	public static DI GetInstance () {
		if (DI._instance == null) {
			DI._instance = new DI();
		}
		return DI._instance;
	}

	public DI () {
		// Registrace v kontejneru
		var builder = new ContainerBuilder();

		var allServices = Assembly.GetExecutingAssembly()
				.GetTypes()
				.Where(t => t.IsClass && !t.IsAbstract && typeof(IService).IsAssignableFrom(t));
		
		builder.RegisterTypes(allServices.Where(t => typeof(ITransient).IsAssignableFrom(t)).ToArray())
			.PropertiesAutowired()
			.AsSelf()
			.AsImplementedInterfaces()
			.InstancePerDependency();

		builder.RegisterTypes(allServices.Where(t => typeof(ISingleton).IsAssignableFrom(t)).ToArray())
			.PropertiesAutowired()
			.AsSelf()
			.AsImplementedInterfaces()
			.SingleInstance();

		this.Container = builder.Build();
	}
}

using Autofac;
using WhisperWriter.DI;
using Xunit;

namespace WhisperWriter.Tests.DI;

public class AutofacExtensionsTests {
	[Fact]
	public void InjectProtectedProperties_ResolvesMarkedPropertiesFromTheScope () {
		var builder = new ContainerBuilder();
		builder.RegisterType<InjectedDependency>().AsSelf().SingleInstance();
		builder.RegisterType<InjectedComponent>()
			.AsSelf()
			.InjectProtectedProperties();

		using var container = builder.Build();
		var component = container.Resolve<InjectedComponent>();

		Assert.Same(container.Resolve<InjectedDependency>(), component.Dependency);
	}

	public class InjectedDependency {
	}

	public class InjectedComponent {
		public InjectedDependency? Dependency => this.injectedDependency;

		[Inject]
		protected InjectedDependency? injectedDependency { get; set; }
	}
}
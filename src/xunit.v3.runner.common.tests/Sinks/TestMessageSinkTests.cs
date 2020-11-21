﻿using System;
using System.Linq;
using System.Reflection;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;
using Xunit.Runner.v2;
using Xunit.v3;

public class TestMessageSinkTests
{
	static readonly MethodInfo forMethodGeneric = typeof(Substitute).GetMethods().Single(m => m.Name == nameof(Substitute.For) && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1);

	[Theory]
	// Diagnostics
	[InlineData(typeof(_DiagnosticMessage))]
	[InlineData(typeof(IErrorMessage))]
	// Discovery
	[InlineData(typeof(_DiscoveryComplete))]
	[InlineData(typeof(_DiscoveryStarting))]
	[InlineData(typeof(_TestCaseDiscovered))]
	// Execution
	[InlineData(typeof(IAfterTestFinished))]
	[InlineData(typeof(IAfterTestStarting))]
	[InlineData(typeof(IBeforeTestFinished))]
	[InlineData(typeof(IBeforeTestStarting))]
	[InlineData(typeof(_TestAssemblyCleanupFailure))]
	[InlineData(typeof(_TestAssemblyFinished))]
	[InlineData(typeof(_TestAssemblyStarting))]
	[InlineData(typeof(_TestCaseCleanupFailure))]
	[InlineData(typeof(_TestCaseFinished))]
	[InlineData(typeof(_TestCaseStarting))]
	[InlineData(typeof(_TestClassCleanupFailure))]
	[InlineData(typeof(ITestClassConstructionFinished))]
	[InlineData(typeof(ITestClassConstructionStarting))]
	[InlineData(typeof(ITestClassDisposeFinished))]
	[InlineData(typeof(ITestClassDisposeStarting))]
	[InlineData(typeof(_TestClassFinished))]
	[InlineData(typeof(_TestClassStarting))]
	[InlineData(typeof(_TestCollectionCleanupFailure))]
	[InlineData(typeof(_TestCollectionFinished))]
	[InlineData(typeof(_TestCollectionStarting))]
	[InlineData(typeof(ITestCleanupFailure))]
	[InlineData(typeof(ITestFailed))]
	[InlineData(typeof(ITestFinished))]
	[InlineData(typeof(_TestMethodCleanupFailure))]
	[InlineData(typeof(_TestMethodFinished))]
	[InlineData(typeof(_TestMethodStarting))]
	[InlineData(typeof(ITestOutput))]
	[InlineData(typeof(ITestPassed))]
	[InlineData(typeof(ITestSkipped))]
	[InlineData(typeof(ITestStarting))]
	// Runner
	[InlineData(typeof(ITestAssemblyExecutionStarting))]
	[InlineData(typeof(ITestAssemblyExecutionFinished))]
	[InlineData(typeof(ITestAssemblyDiscoveryStarting))]
	[InlineData(typeof(ITestAssemblyDiscoveryFinished))]
	[InlineData(typeof(ITestExecutionSummary))]
	public void ProcessesVisitorTypes(Type type)
	{
		var forMethod = forMethodGeneric.MakeGenericMethod(type);
		var substitute = (IMessageSinkMessage)forMethod.Invoke(null, new object[] { new object[0] })!;
		var sink = new SpyTestMessageSink();

		sink.OnMessage(substitute);

		Assert.Collection(
			sink.Calls,
			msg => Assert.Equal(type.Name, msg)
		);
	}
}

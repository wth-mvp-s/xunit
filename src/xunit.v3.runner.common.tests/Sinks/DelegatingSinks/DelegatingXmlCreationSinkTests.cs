﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;
using Xunit.Runner.Common;
using Xunit.Runner.v2;
using Xunit.Sdk;
using Xunit.v3;

public class DelegatingXmlCreationSinkTests
{
	readonly ExecutionSummary executionSummary = new ExecutionSummary();
	readonly IExecutionSink innerSink;

	public DelegatingXmlCreationSinkTests()
	{
		innerSink = Substitute.For<IExecutionSink>();
		innerSink.ExecutionSummary.Returns(executionSummary);
	}

	[Fact]
	public void AddsAssemblyStartingInformationToXml()
	{
		var assemblyStarting = new _TestAssemblyStarting
		{
			AssemblyPath = "assembly",
			AssemblyUniqueID = "assembly-id",
			ConfigFilePath = "config",
			StartTime = new DateTimeOffset(2013, 7, 6, 16, 24, 32, TimeSpan.Zero),
			TargetFramework = "MentalFloss,Version=v21.12",
			TestEnvironment = "256-bit MentalFloss",
			TestFrameworkDisplayName = "xUnit.net v14.42"
		};

		var assemblyElement = new XElement("assembly");
		var sink = new DelegatingXmlCreationSink(innerSink, assemblyElement);

		sink.OnMessage(assemblyStarting);

		Assert.Equal("assembly", assemblyElement.Attribute("name").Value);
		Assert.Equal("MentalFloss,Version=v21.12", assemblyElement.Attribute("target-framework").Value);
		Assert.Equal("256-bit MentalFloss", assemblyElement.Attribute("environment").Value);
		Assert.Equal("xUnit.net v14.42", assemblyElement.Attribute("test-framework").Value);
		Assert.Equal("config", assemblyElement.Attribute("config-file").Value);
		Assert.Equal("2013-07-06", assemblyElement.Attribute("run-date").Value);
		Assert.Equal("16:24:32", assemblyElement.Attribute("run-time").Value);
	}

	[Fact]
	public void AssemblyStartingDoesNotIncludeNullValues()
	{
		var assemblyStarting = new _TestAssemblyStarting
		{
			AssemblyPath = "assembly",
			AssemblyUniqueID = "assembly-id",
			StartTime = new DateTimeOffset(2013, 7, 6, 16, 24, 32, TimeSpan.Zero),
			TestEnvironment = "256-bit MentalFloss",
			TestFrameworkDisplayName = "xUnit.net v14.42"
		};

		var assemblyElement = new XElement("assembly");
		var sink = new DelegatingXmlCreationSink(innerSink, assemblyElement);

		sink.OnMessage(assemblyStarting);

		Assert.Null(assemblyElement.Attribute("config-file"));
		Assert.Null(assemblyElement.Attribute("target-framework"));
	}

	[CulturedFact]
	public void AddsAssemblyFinishedInformationToXml()
	{
		executionSummary.Total = 2112;
		executionSummary.Failed = 42;
		executionSummary.Skipped = 6;
		executionSummary.Time = 123.4567M;
		executionSummary.Errors = 1;

		var assemblyFinished = Mocks.TestAssemblyFinished();
		var assemblyElement = new XElement("assembly");
		var sink = new DelegatingXmlCreationSink(innerSink, assemblyElement);
		var errorMessage = Substitute.For<IErrorMessage>();
		errorMessage.ExceptionTypes.Returns(new[] { "ExceptionType" });
		errorMessage.Messages.Returns(new[] { "Message" });
		errorMessage.StackTraces.Returns(new[] { "Stack" });

		sink.OnMessage(errorMessage);
		sink.OnMessage(assemblyFinished);

		Assert.Equal("2112", assemblyElement.Attribute("total").Value);
		Assert.Equal("2064", assemblyElement.Attribute("passed").Value);
		Assert.Equal("42", assemblyElement.Attribute("failed").Value);
		Assert.Equal("6", assemblyElement.Attribute("skipped").Value);
		Assert.Equal(123.457M.ToString(CultureInfo.InvariantCulture), assemblyElement.Attribute("time").Value);
		Assert.Equal("1", assemblyElement.Attribute("errors").Value);
	}

	[CulturedFact]
	public void AddsTestCollectionElementsToXml()
	{
		var assemblyFinished = Mocks.TestAssemblyFinished();
		var testCollectionStarted = Mocks.TestCollectionStarting(testCollectionDisplayName: "Collection Name", testCollectionUniqueID: "abc123");
		var testCollectionFinished = Mocks.TestCollectionFinished(2112, 42, 6, 123.4567m, testCollectionUniqueID: "abc123");

		var assemblyElement = new XElement("assembly");
		var sink = new DelegatingXmlCreationSink(innerSink, assemblyElement);

		sink.OnMessage(testCollectionStarted);
		sink.OnMessage(testCollectionFinished);
		sink.OnMessage(assemblyFinished);

		var collectionElement = Assert.Single(assemblyElement.Elements("collection"));
		Assert.Equal("Collection Name", collectionElement.Attribute("name").Value);
		Assert.Equal("2112", collectionElement.Attribute("total").Value);
		Assert.Equal("2064", collectionElement.Attribute("passed").Value);
		Assert.Equal("42", collectionElement.Attribute("failed").Value);
		Assert.Equal("6", collectionElement.Attribute("skipped").Value);
		Assert.Equal(123.457M.ToString(CultureInfo.InvariantCulture), collectionElement.Attribute("time").Value);
	}

	[CulturedFact]
	public void AddsPassingTestElementToXml()
	{
		var assemblyFinished = Mocks.TestAssemblyFinished();
		var testCase = Mocks.TestCase<ClassUnderTest>("TestMethod");
		testCase.SourceInformation.Returns(new SourceInformation());
		var test = Mocks.Test(testCase, "Test Display Name");
		var testPassed = Substitute.For<ITestPassed>();
		testPassed.TestCase.Returns(testCase);
		testPassed.Test.Returns(test);
		testPassed.ExecutionTime.Returns(123.4567809M);
		testPassed.Output.Returns("test output");

		var assemblyElement = new XElement("assembly");
		var sink = new DelegatingXmlCreationSink(innerSink, assemblyElement);

		sink.OnMessage(testPassed);
		sink.OnMessage(assemblyFinished);

		var testElement = Assert.Single(assemblyElement.Elements("collection").Single().Elements("test"));
		Assert.Equal("Test Display Name", testElement.Attribute("name").Value);
		Assert.Equal("DelegatingXmlCreationSinkTests+ClassUnderTest", testElement.Attribute("type").Value);
		Assert.Equal("TestMethod", testElement.Attribute("method").Value);
		Assert.Equal("Pass", testElement.Attribute("result").Value);
		Assert.Equal(123.4567809M.ToString(CultureInfo.InvariantCulture), testElement.Attribute("time").Value);
		Assert.Equal("test output", testElement.Element("output").Value);
		Assert.Null(testElement.Attribute("source-file"));
		Assert.Null(testElement.Attribute("source-line"));
		Assert.Empty(testElement.Elements("traits"));
		Assert.Empty(testElement.Elements("failure"));
		Assert.Empty(testElement.Elements("reason"));
	}

	[CulturedFact]
	public void EmptyOutputStringDoesNotShowUpInResultingXml()
	{
		var assemblyFinished = Mocks.TestAssemblyFinished();
		var testCase = Mocks.TestCase<ClassUnderTest>("TestMethod");
		testCase.SourceInformation.Returns(new SourceInformation());
		var test = Mocks.Test(testCase, "Test Display Name");
		var testPassed = Substitute.For<ITestPassed>();
		testPassed.TestCase.Returns(testCase);
		testPassed.Test.Returns(test);
		testPassed.ExecutionTime.Returns(123.4567809M);
		testPassed.Output.Returns(string.Empty);

		var assemblyElement = new XElement("assembly");
		var sink = new DelegatingXmlCreationSink(innerSink, assemblyElement);

		sink.OnMessage(testPassed);
		sink.OnMessage(assemblyFinished);

		var testElement = Assert.Single(assemblyElement.Elements("collection").Single().Elements("test"));
		Assert.Equal("Test Display Name", testElement.Attribute("name").Value);
		Assert.Equal("DelegatingXmlCreationSinkTests+ClassUnderTest", testElement.Attribute("type").Value);
		Assert.Equal("TestMethod", testElement.Attribute("method").Value);
		Assert.Equal("Pass", testElement.Attribute("result").Value);
		Assert.Equal(123.4567809M.ToString(CultureInfo.InvariantCulture), testElement.Attribute("time").Value);
		Assert.Null(testElement.Attribute("output"));
		Assert.Null(testElement.Attribute("source-file"));
		Assert.Null(testElement.Attribute("source-line"));
		Assert.Empty(testElement.Elements("traits"));
		Assert.Empty(testElement.Elements("failure"));
		Assert.Empty(testElement.Elements("reason"));
	}

	[CulturedFact]
	public void AddsFailingTestElementToXml()
	{
		var assemblyFinished = Mocks.TestAssemblyFinished();
		var testCase = Mocks.TestCase<ClassUnderTest>("TestMethod");
		var test = Mocks.Test(testCase, "Test Display Name");
		var testFailed = Substitute.For<ITestFailed>();
		testFailed.TestCase.Returns(testCase);
		testFailed.Test.Returns(test);
		testFailed.ExecutionTime.Returns(123.4567809M);
		testFailed.Output.Returns("test output");
		testFailed.ExceptionTypes.Returns(new[] { "Exception Type" });
		testFailed.Messages.Returns(new[] { "Exception Message" });
		testFailed.StackTraces.Returns(new[] { "Exception Stack Trace" });

		var assemblyElement = new XElement("assembly");
		var sink = new DelegatingXmlCreationSink(innerSink, assemblyElement);

		sink.OnMessage(testFailed);
		sink.OnMessage(assemblyFinished);

		var testElement = Assert.Single(assemblyElement.Elements("collection").Single().Elements("test"));
		Assert.Equal("Test Display Name", testElement.Attribute("name").Value);
		Assert.Equal("DelegatingXmlCreationSinkTests+ClassUnderTest", testElement.Attribute("type").Value);
		Assert.Equal("TestMethod", testElement.Attribute("method").Value);
		Assert.Equal("Fail", testElement.Attribute("result").Value);
		Assert.Equal(123.4567809M.ToString(CultureInfo.InvariantCulture), testElement.Attribute("time").Value);
		Assert.Equal("test output", testElement.Element("output").Value);
		var failureElement = Assert.Single(testElement.Elements("failure"));
		Assert.Equal("Exception Type", failureElement.Attribute("exception-type").Value);
		Assert.Equal("Exception Type : Exception Message", failureElement.Elements("message").Single().Value);
		Assert.Equal("Exception Stack Trace", failureElement.Elements("stack-trace").Single().Value);
		Assert.Empty(testElement.Elements("reason"));
	}

	[Fact]
	public void NullStackTraceInFailedTestResultsInEmptyStackTraceXmlElement()
	{
		var assemblyFinished = Mocks.TestAssemblyFinished();
		var testCase = Mocks.TestCase<ClassUnderTest>("TestMethod");
		var testFailed = Substitute.For<ITestFailed>();
		testFailed.TestCase.Returns(testCase);
		testFailed.ExceptionTypes.Returns(new[] { "ExceptionType" });
		testFailed.Messages.Returns(new[] { "Exception Message" });
		testFailed.StackTraces.Returns(new[] { (string?)null });
		testFailed.ExceptionParentIndices.Returns(new[] { -1 });

		var assemblyElement = new XElement("assembly");
		var sink = new DelegatingXmlCreationSink(innerSink, assemblyElement);

		sink.OnMessage(testFailed);
		sink.OnMessage(assemblyFinished);

		var testElement = Assert.Single(assemblyElement.Elements("collection").Single().Elements("test"));
		var failureElement = Assert.Single(testElement.Elements("failure"));
		Assert.Empty(failureElement.Elements("stack-trace").Single().Value);
	}

	[CulturedFact]
	public void AddsSkippedTestElementToXml()
	{
		var assemblyFinished = Mocks.TestAssemblyFinished();
		var testCase = Mocks.TestCase<ClassUnderTest>("TestMethod");
		var test = Mocks.Test(testCase, "Test Display Name");
		var testSkipped = Substitute.For<ITestSkipped>();
		testSkipped.TestCase.Returns(testCase);
		testSkipped.Test.Returns(test);
		testSkipped.ExecutionTime.Returns(0.0M);
		testSkipped.Reason.Returns("Skip Reason");

		var assemblyElement = new XElement("assembly");
		var sink = new DelegatingXmlCreationSink(innerSink, assemblyElement);

		sink.OnMessage(testSkipped);
		sink.OnMessage(assemblyFinished);

		var testElement = Assert.Single(assemblyElement.Elements("collection").Single().Elements("test"));
		Assert.Equal("Test Display Name", testElement.Attribute("name").Value);
		Assert.Equal("DelegatingXmlCreationSinkTests+ClassUnderTest", testElement.Attribute("type").Value);
		Assert.Equal("TestMethod", testElement.Attribute("method").Value);
		Assert.Equal("Skip", testElement.Attribute("result").Value);
		Assert.Equal(0.0M.ToString(CultureInfo.InvariantCulture), testElement.Attribute("time").Value);
		var reasonElement = Assert.Single(testElement.Elements("reason"));
		Assert.Equal("Skip Reason", reasonElement.Value);
		Assert.Empty(testElement.Elements("failure"));
	}

	[Fact]
	public void TestElementSourceInfoIsPlacedInXmlWhenPresent()
	{
		var assemblyFinished = Mocks.TestAssemblyFinished();
		var testCase = Mocks.TestCase<ClassUnderTest>("TestMethod");
		testCase.SourceInformation.Returns(new SourceInformation { FileName = "source file", LineNumber = 42 });
		var testPassed = Substitute.For<ITestPassed>();
		testPassed.TestCase.Returns(testCase);

		var assemblyElement = new XElement("assembly");
		var sink = new DelegatingXmlCreationSink(innerSink, assemblyElement);

		sink.OnMessage(testPassed);
		sink.OnMessage(assemblyFinished);

		var testElement = Assert.Single(assemblyElement.Elements("collection").Single().Elements("test"));
		Assert.Equal("source file", testElement.Attribute("source-file").Value);
		Assert.Equal("42", testElement.Attribute("source-line").Value);
	}

	[Fact]
	public void TestElementTraisArePlacedInXmlWhenPresent()
	{
		var traits = new Dictionary<string, List<string>>
		{
			{ "name1", new List<string> { "value1" }},
			{ "name2", new List<string> { "value2" }}
		};
		var assemblyFinished = Mocks.TestAssemblyFinished();
		var passingTestCase = Mocks.TestCase<ClassUnderTest>("TestMethod");
		passingTestCase.Traits.Returns(traits);
		var testPassed = Substitute.For<ITestPassed>();
		testPassed.TestCase.Returns(passingTestCase);

		var assemblyElement = new XElement("assembly");
		var sink = new DelegatingXmlCreationSink(innerSink, assemblyElement);

		sink.OnMessage(testPassed);
		sink.OnMessage(assemblyFinished);

		var traitsElements = assemblyElement.Elements("collection").Single().Elements("test").Single().Elements("traits").Single().Elements("trait");
		var name1Element = Assert.Single(traitsElements, e => e.Attribute("name").Value == "name1");
		Assert.Equal("value1", name1Element.Attribute("value").Value);
		var name2Element = Assert.Single(traitsElements, e => e.Attribute("name").Value == "name2");
		Assert.Equal("value2", name2Element.Attribute("value").Value);
	}

	public static IEnumerable<object[]> IllegalXmlTestData()
	{
		yield return new object[]
		{
			new string(Enumerable.Range(0, 32).Select(x => (char)x).ToArray()),
			@"\0\x01\x02\x03\x04\x05\x06\a\b\t\n\v\f\r\x0e\x0f\x10\x11\x12\x13\x14\x15\x16\x17\x18\x19\x1a\x1b\x1c\x1d\x1e\x1f"
		};
		// Invalid surrogate characters should be added as \x----, where ---- is the hex value of the char
		yield return new object[]
		{
			"\xd800 Hello.World \xdc00",
			@"\xd800 Hello.World \xdc00"
		};
		// ...but valid ones should be outputted like normal
		yield return new object[]
		{
			"\xd800\xdfff This.Is.Valid \xda00\xdd00",
			"\xd800\xdfff This.Is.Valid \xda00\xdd00" // note: no @
		};
	}

	[Theory]
	[MemberData(nameof(IllegalXmlTestData))]
	public void IllegalXmlDoesNotPreventXmlFromBeingSaved(
		string inputName,
		string outputName)
	{
		var assemblyFinished = Mocks.TestAssemblyFinished();
		var testCase = Mocks.TestCase<ClassUnderTest>("TestMethod");
		var test = Mocks.Test(testCase, inputName);
		var testSkipped = Substitute.For<ITestSkipped>();
		testSkipped.TestCase.Returns(testCase);
		testSkipped.Test.Returns(test);
		testSkipped.Reason.Returns("Bad\0\r\nString");

		var assemblyElement = new XElement("assembly");
		var sink = new DelegatingXmlCreationSink(innerSink, assemblyElement);

		sink.OnMessage(testSkipped);
		sink.OnMessage(assemblyFinished);

		using var writer = new StringWriter();
		assemblyElement.Save(writer, SaveOptions.DisableFormatting);

		var outputXml = writer.ToString();
		Assert.Equal($@"<?xml version=""1.0"" encoding=""utf-16""?><assembly total=""0"" passed=""0"" failed=""0"" skipped=""0"" time=""0.000"" errors=""0""><errors /><collection><test name=""{outputName}"" type=""DelegatingXmlCreationSinkTests+ClassUnderTest"" method=""TestMethod"" time=""0"" result=""Skip"" source-file=""""><reason><![CDATA[Bad\0\r\nString]]></reason></test></collection></assembly>", outputXml);
	}

	class ClassUnderTest
	{
		[Fact]
		public void TestMethod() { }
	}

	readonly string assemblyID = "assembly-id";
	readonly string classID = "test-class-id";
	readonly string collectionID = "test-collection-id";
	readonly int[] exceptionParentIndices = new[] { -1 };
	readonly string[] exceptionTypes = new[] { "ExceptionType" };
	readonly string[] messages = new[] { "This is my message \t\r\n" };
	readonly string methodID = "test-method-id";
	readonly string[] stackTraces = new[] { "Line 1\r\nLine 2\r\nLine 3" };
	readonly string testCaseID = "test-case-id";
	//readonly string testID = "test-id";

	static TMessageType MakeFailureInformationSubstitute<TMessageType>()
		where TMessageType : class, IFailureInformation
	{
		var result = Substitute.For<TMessageType, InterfaceProxy<TMessageType>>();
		result.ExceptionTypes.Returns(new[] { "ExceptionType" });
		result.Messages.Returns(new[] { "This is my message \t\r\n" });
		result.StackTraces.Returns(new[] { "Line 1\r\nLine 2\r\nLine 3" });
		return result;
	}

	public static IEnumerable<object?[]> Messages
	{
		get
		{
			yield return new object?[] { MakeFailureInformationSubstitute<IErrorMessage>(), "fatal", null };

			var testCase = Mocks.TestCase(typeof(object), "ToString", displayName: "MyTestCase");
			var testCleanupFailure = MakeFailureInformationSubstitute<ITestCleanupFailure>();
			var test = Mocks.Test(testCase, "MyTest");
			testCleanupFailure.Test.Returns(test);
			yield return new object?[] { testCleanupFailure, "test-cleanup", "MyTest" };
		}
	}

	[Fact]
	public void TestAssemblyCleanupFailure()
	{
		var collectionStarting = new _TestAssemblyStarting
		{
			AssemblyUniqueID = assemblyID,
			AssemblyName = "assembly-name",
			AssemblyPath = "assembly-file-path",
			ConfigFilePath = "config-file-path",
			StartTime = DateTimeOffset.UtcNow,
			TargetFramework = "target-framework",
			TestEnvironment = "test-environment",
			TestFrameworkDisplayName = "test-framework"
		};
		var collectionCleanupFailure = new _TestAssemblyCleanupFailure
		{
			AssemblyUniqueID = assemblyID,
			ExceptionParentIndices = exceptionParentIndices,
			ExceptionTypes = exceptionTypes,
			Messages = messages,
			StackTraces = stackTraces
		};
		var assemblyElement = new XElement("assembly");
		var sink = new DelegatingXmlCreationSink(innerSink, assemblyElement);

		sink.OnMessage(collectionStarting);
		sink.OnMessage(collectionCleanupFailure);

		AssertFailureElement(assemblyElement, "assembly-cleanup", "assembly-file-path");
	}

	[Fact]
	public void TestCaseCleanupFailure()
	{
		var caseStarting = new _TestCaseStarting
		{
			AssemblyUniqueID = assemblyID,
			TestCaseUniqueID = testCaseID,
			TestCaseDisplayName = "MyTestCase",
			TestClassUniqueID = classID,
			TestCollectionUniqueID = collectionID,
			TestMethodUniqueID = methodID
		};
		var caseCleanupFailure = new _TestCaseCleanupFailure
		{
			AssemblyUniqueID = assemblyID,
			ExceptionParentIndices = exceptionParentIndices,
			ExceptionTypes = exceptionTypes,
			Messages = messages,
			StackTraces = stackTraces,
			TestCaseUniqueID = testCaseID,
			TestCollectionUniqueID = collectionID,
			TestClassUniqueID = classID,
			TestMethodUniqueID = methodID
		};
		var assemblyElement = new XElement("assembly");
		var sink = new DelegatingXmlCreationSink(innerSink, assemblyElement);

		sink.OnMessage(caseStarting);
		sink.OnMessage(caseCleanupFailure);

		AssertFailureElement(assemblyElement, "test-case-cleanup", "MyTestCase");
	}

	[Fact]
	public void TestClassCleanupFailure()
	{
		var classStarting = new _TestClassStarting
		{
			AssemblyUniqueID = assemblyID,
			TestClass = "MyType",
			TestClassUniqueID = classID,
			TestCollectionUniqueID = collectionID
		};
		var classCleanupFailure = new _TestClassCleanupFailure
		{
			AssemblyUniqueID = assemblyID,
			ExceptionParentIndices = exceptionParentIndices,
			ExceptionTypes = exceptionTypes,
			Messages = messages,
			StackTraces = stackTraces,
			TestCollectionUniqueID = collectionID,
			TestClassUniqueID = classID
		};
		var assemblyElement = new XElement("assembly");
		var sink = new DelegatingXmlCreationSink(innerSink, assemblyElement);

		sink.OnMessage(classStarting);
		sink.OnMessage(classCleanupFailure);

		AssertFailureElement(assemblyElement, "test-class-cleanup", "MyType");
	}

	[Fact]
	public void TestCollectionCleanupFailure()
	{
		var collectionStarting = new _TestCollectionStarting
		{
			AssemblyUniqueID = assemblyID,
			TestCollectionDisplayName = "FooBar",
			TestCollectionUniqueID = collectionID
		};
		var collectionCleanupFailure = new _TestCollectionCleanupFailure
		{
			AssemblyUniqueID = assemblyID,
			ExceptionParentIndices = exceptionParentIndices,
			ExceptionTypes = exceptionTypes,
			Messages = messages,
			StackTraces = stackTraces,
			TestCollectionUniqueID = collectionID
		};
		var assemblyElement = new XElement("assembly");
		var sink = new DelegatingXmlCreationSink(innerSink, assemblyElement);

		sink.OnMessage(collectionStarting);
		sink.OnMessage(collectionCleanupFailure);

		AssertFailureElement(assemblyElement, "test-collection-cleanup", "FooBar");
	}

	[Fact]
	public void TestMethodCleanupFailure()
	{
		var methodStarting = new _TestMethodStarting
		{
			AssemblyUniqueID = assemblyID,
			TestClassUniqueID = classID,
			TestCollectionUniqueID = collectionID,
			TestMethod = "MyMethod",
			TestMethodUniqueID = methodID,
		};
		var methodCleanupFailure = new _TestMethodCleanupFailure
		{
			AssemblyUniqueID = assemblyID,
			ExceptionParentIndices = exceptionParentIndices,
			ExceptionTypes = exceptionTypes,
			Messages = messages,
			StackTraces = stackTraces,
			TestCollectionUniqueID = collectionID,
			TestClassUniqueID = classID,
			TestMethodUniqueID = methodID
		};
		var assemblyElement = new XElement("assembly");
		var sink = new DelegatingXmlCreationSink(innerSink, assemblyElement);

		sink.OnMessage(methodStarting);
		sink.OnMessage(methodCleanupFailure);

		AssertFailureElement(assemblyElement, "test-method-cleanup", "MyMethod");
	}

	[Theory]
	[MemberData("Messages", DisableDiscoveryEnumeration = true)]
	public void AddsErrorMessagesToXml(
		IMessageSinkMessage errorMessage,
		string messageType,
		string name)
	{
		var assemblyFinished = Mocks.TestAssemblyFinished();
		var assemblyElement = new XElement("assembly");
		var sink = new DelegatingXmlCreationSink(innerSink, assemblyElement);

		sink.OnMessage(errorMessage);
		sink.OnMessage(assemblyFinished);

		AssertFailureElement(assemblyElement, messageType, name);
	}

	static void AssertFailureElement(XElement assemblyElement, string messageType, string name)
	{
		var errorElement = Assert.Single(assemblyElement.Element("errors").Elements());
		Assert.Equal(messageType, errorElement.Attribute("type").Value);

		if (name == null)
			Assert.Null(errorElement.Attribute("name"));
		else
			Assert.Equal(name, errorElement.Attribute("name").Value);

		var failureElement = Assert.Single(errorElement.Elements("failure"));
		Assert.Equal("ExceptionType", failureElement.Attribute("exception-type").Value);
		Assert.Equal("ExceptionType : This is my message \\t\\r\\n", failureElement.Elements("message").Single().Value);
		Assert.Equal("Line 1\r\nLine 2\r\nLine 3", failureElement.Elements("stack-trace").Single().Value);
	}
}

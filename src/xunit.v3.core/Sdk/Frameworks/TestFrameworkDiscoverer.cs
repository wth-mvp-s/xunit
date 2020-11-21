﻿using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;
using Xunit.Internal;
using Xunit.Runner.v2;
using Xunit.v3;

namespace Xunit.Sdk
{
	/// <summary>
	/// A base implementation of <see cref="_ITestFrameworkDiscoverer"/> that supports test filtering
	/// and runs the discovery process on a thread pool thread.
	/// </summary>
	public abstract class TestFrameworkDiscoverer : _ITestFrameworkDiscoverer, IAsyncDisposable
	{
		IAssemblyInfo assemblyInfo;
		string? configFileName;
		_IMessageSink diagnosticMessageSink;
		bool disposed;
		_ISourceInformationProvider sourceProvider;
		readonly Lazy<string> targetFramework;

		/// <summary>
		/// Initializes a new instance of the <see cref="TestFrameworkDiscoverer"/> class.
		/// </summary>
		/// <param name="assemblyInfo">The test assembly.</param>
		/// <param name="configFileName">The configuration filename.</param>
		/// <param name="sourceProvider">The source information provider.</param>
		/// <param name="diagnosticMessageSink">The message sink which receives <see cref="_DiagnosticMessage"/> messages.</param>
		protected TestFrameworkDiscoverer(
			IAssemblyInfo assemblyInfo,
			string? configFileName,
			_ISourceInformationProvider sourceProvider,
			_IMessageSink diagnosticMessageSink)
		{
			this.assemblyInfo = Guard.ArgumentNotNull(nameof(assemblyInfo), assemblyInfo);
			this.configFileName = configFileName;
			this.diagnosticMessageSink = Guard.ArgumentNotNull(nameof(diagnosticMessageSink), diagnosticMessageSink);
			this.sourceProvider = Guard.ArgumentNotNull(nameof(sourceProvider), sourceProvider);

			targetFramework = new Lazy<string>(() =>
			{
				string? result = null;

				var attrib = AssemblyInfo.GetCustomAttributes(typeof(TargetFrameworkAttribute)).FirstOrDefault();
				if (attrib != null)
					result = attrib.GetConstructorArguments().Cast<string>().First();

				return result ?? "";
			});
		}

		/// <summary>
		/// Gets the assembly that's being discovered.
		/// </summary>
		protected internal IAssemblyInfo AssemblyInfo
		{
			get => assemblyInfo;
			set => assemblyInfo = Guard.ArgumentNotNull(nameof(AssemblyInfo), value);
		}

		/// <summary>
		/// Gets the message sink used to report diagnostic messages.
		/// </summary>
		protected _IMessageSink DiagnosticMessageSink
		{
			get => diagnosticMessageSink;
			set => diagnosticMessageSink = Guard.ArgumentNotNull(nameof(DiagnosticMessageSink), value);
		}

		/// <summary>
		/// Gets the disposal tracker for the test framework discoverer.
		/// </summary>
		protected DisposalTracker DisposalTracker { get; } = new DisposalTracker();

		/// <summary>
		/// Get the source code information provider used during discovery.
		/// </summary>
		protected _ISourceInformationProvider SourceProvider
		{
			get => sourceProvider;
			set => sourceProvider = Guard.ArgumentNotNull(nameof(SourceProvider), value);
		}

		/// <summary>
		/// Gets the unique ID for the test assembly under test.
		/// </summary>
		public abstract string TestAssemblyUniqueID { get; }

		/// <inheritdoc/>
		public string TargetFramework => targetFramework.Value;

		/// <inheritdoc/>
		public abstract string TestFrameworkDisplayName { get; }

		/// <summary>
		/// Implement this method to create a test class for the given CLR type.
		/// </summary>
		/// <param name="class">The CLR type.</param>
		/// <returns>The test class.</returns>
		protected internal abstract ITestClass CreateTestClass(ITypeInfo @class);

		/// <inheritdoc/>
		public virtual ValueTask DisposeAsync()
		{
			if (disposed)
				throw new ObjectDisposedException(GetType().FullName);

			disposed = true;

			return DisposalTracker.DisposeAsync();
		}

		/// <inheritdoc/>
		public void Find(
			bool includeSourceInformation,
			_IMessageSink discoveryMessageSink,
			_ITestFrameworkDiscoveryOptions discoveryOptions)
		{
			Guard.ArgumentNotNull("discoveryMessageSink", discoveryMessageSink);
			Guard.ArgumentNotNull("discoveryOptions", discoveryOptions);

			ThreadPool.QueueUserWorkItem(_ =>
			{
				using (var messageBus = CreateMessageBus(discoveryMessageSink, discoveryOptions))
				using (new PreserveWorkingFolder(AssemblyInfo))
				{
					var discoveryStarting = new _DiscoveryStarting
					{
						AssemblyName = AssemblyInfo.Name,
						AssemblyPath = AssemblyInfo.AssemblyPath,
						AssemblyUniqueID = TestAssemblyUniqueID,
						ConfigFilePath = configFileName
					};
					messageBus.QueueMessage(discoveryStarting);

					foreach (var type in AssemblyInfo.GetTypes(false).Where(IsValidTestClass))
					{
						var testClass = CreateTestClass(type);
						if (!FindTestsForTypeAndWrapExceptions(testClass, includeSourceInformation, messageBus, discoveryOptions))
							break;
					}

					var discoveryComplete = new _DiscoveryComplete { AssemblyUniqueID = TestAssemblyUniqueID };
					messageBus.QueueMessage(discoveryComplete);
				}
			});
		}

		static IMessageBus CreateMessageBus(
			_IMessageSink messageSink,
			_ITestFrameworkDiscoveryOptions options)
		{
			if (options.SynchronousMessageReportingOrDefault())
				return new SynchronousMessageBus(messageSink);

			return new MessageBus(messageSink);
		}

		/// <inheritdoc/>
		public void Find(
			string typeName,
			bool includeSourceInformation,
			_IMessageSink discoveryMessageSink,
			_ITestFrameworkDiscoveryOptions discoveryOptions)
		{
			Guard.ArgumentNotNullOrEmpty("typeName", typeName);
			Guard.ArgumentNotNull("discoveryMessageSink", discoveryMessageSink);
			Guard.ArgumentNotNull("discoveryOptions", discoveryOptions);

			ThreadPool.QueueUserWorkItem(_ =>
			{
				using (var messageBus = CreateMessageBus(discoveryMessageSink, discoveryOptions))
				using (new PreserveWorkingFolder(AssemblyInfo))
				{
					var discoveryStarting = new _DiscoveryStarting
					{
						AssemblyName = AssemblyInfo.Name,
						AssemblyPath = AssemblyInfo.AssemblyPath,
						AssemblyUniqueID = TestAssemblyUniqueID,
						ConfigFilePath = configFileName
					};
					messageBus.QueueMessage(discoveryStarting);

					var typeInfo = AssemblyInfo.GetType(typeName);
					if (typeInfo != null && IsValidTestClass(typeInfo))
					{
						var testClass = CreateTestClass(typeInfo);
						FindTestsForTypeAndWrapExceptions(testClass, includeSourceInformation, messageBus, discoveryOptions);
					}

					var discoveryComplete = new _DiscoveryComplete { AssemblyUniqueID = TestAssemblyUniqueID };
					messageBus.QueueMessage(discoveryComplete);
				}
			});
		}

		/// <summary>
		/// Core implementation to discover unit tests in a given test class.
		/// </summary>
		/// <param name="testCollectionUniqueID">The test collection unique ID.</param>
		/// <param name="testClassUniqueID">The test class unique ID.</param>
		/// <param name="testClass">The test class.</param>
		/// <param name="includeSourceInformation">Set to <c>true</c> to attempt to include source information.</param>
		/// <param name="messageBus">The message sink to send discovery messages to.</param>
		/// <param name="discoveryOptions">The options used by the test framework during discovery.</param>
		/// <returns>Returns <c>true</c> if discovery should continue; <c>false</c> otherwise.</returns>
		protected abstract bool FindTestsForType(
			string testCollectionUniqueID,
			string? testClassUniqueID,
			ITestClass testClass,
			bool includeSourceInformation,
			IMessageBus messageBus,
			_ITestFrameworkDiscoveryOptions discoveryOptions
		);

		bool FindTestsForTypeAndWrapExceptions(
			ITestClass testClass,
			bool includeSourceInformation,
			IMessageBus messageBus,
			_ITestFrameworkDiscoveryOptions discoveryOptions)
		{
			try
			{
				var testCollectionUniqueID = FactDiscoverer.ComputeUniqueID(TestAssemblyUniqueID, testClass.TestCollection);
				var testClassUniqueID = FactDiscoverer.ComputeUniqueID(testCollectionUniqueID, testClass);
				return FindTestsForType(testCollectionUniqueID, testClassUniqueID, testClass, includeSourceInformation, messageBus, discoveryOptions);
			}
			catch (Exception ex)
			{
				DiagnosticMessageSink.OnMessage(new _DiagnosticMessage { Message = $"Exception during discovery:{Environment.NewLine}{ex}" });
				return true; // Keep going on to the next type
			}
		}

		bool IsEmpty(ISourceInformation sourceInformation) =>
			sourceInformation == null || (string.IsNullOrWhiteSpace(sourceInformation.FileName) && sourceInformation.LineNumber == null);

		/// <summary>
		/// Determines if a type should be used for discovery. Can be used to filter out types that
		/// are not desirable. The default implementation filters out abstract (non-static) classes.
		/// </summary>
		/// <param name="type">The type.</param>
		/// <returns>Returns <c>true</c> if the type can contain tests; <c>false</c>, otherwise.</returns>
		protected virtual bool IsValidTestClass(ITypeInfo type)
		{
			Guard.ArgumentNotNull(nameof(type), type);

			return !type.IsAbstract || type.IsSealed;
		}

		/// <summary>
		/// Reports a discovered test case to the message bus, after updating the source code information
		/// (if desired and not already provided).
		/// </summary>
		/// <param name="testCase">The test case to report</param>
		/// <param name="includeSourceInformation">A flag to indicate whether source information is desired</param>
		/// <param name="messageBus">The message bus to report to the test case to</param>
		/// <returns>Returns the result from calling <see cref="IMessageBus.QueueMessage(IMessageSinkMessage)"/>.</returns>
		protected bool ReportDiscoveredTestCase(
			ITestCase testCase,
			bool includeSourceInformation,
			IMessageBus messageBus)
		{
			Guard.ArgumentNotNull(nameof(testCase), testCase);
			Guard.ArgumentNotNull(nameof(messageBus), messageBus);

			if (includeSourceInformation && SourceProvider != null && IsEmpty(testCase.SourceInformation))
			{
				var result = SourceProvider.GetSourceInformation(testCase.TestMethod.TestClass.Class.Name, testCase.TestMethod.Method.Name);
				testCase.SourceInformation = new SourceInformation { FileName = result.FileName, LineNumber = result.LineNumber };
			}

			return messageBus.QueueMessage(new _TestCaseDiscovered(testCase));
		}

		/// <inheritdoc/>
		public virtual string Serialize(ITestCase testCase)
		{
			Guard.ArgumentNotNull(nameof(testCase), testCase);

			return SerializationHelper.Serialize(testCase);
		}

		class PreserveWorkingFolder : IDisposable
		{
			readonly string originalWorkingFolder;

			public PreserveWorkingFolder(IAssemblyInfo assemblyInfo)
			{
				originalWorkingFolder = Directory.GetCurrentDirectory();

				if (!string.IsNullOrEmpty(assemblyInfo.AssemblyPath))
					Directory.SetCurrentDirectory(Path.GetDirectoryName(assemblyInfo.AssemblyPath));
			}

			public void Dispose()
			{
				try
				{
					Directory.SetCurrentDirectory(originalWorkingFolder);
				}
				catch { }
			}
		}
	}
}

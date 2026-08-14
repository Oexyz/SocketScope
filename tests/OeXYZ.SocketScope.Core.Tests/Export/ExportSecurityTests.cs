using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using OeXYZ.SocketScope.Core.Domain;
using OeXYZ.SocketScope.Core.Export;

namespace OeXYZ.SocketScope.Core.Tests.Export;

public sealed class ExportSecurityTests
{
    [Fact]
    public void FullReport_PreservesAvailableDiagnosticValues()
    {
        InventoryGeneration generation = TestData.Generation(
            TestData.Observation(
                id: "full",
                localAddress: "192.168.1.50",
                remoteAddress: "203.0.113.9",
                processName: "example-service",
                executablePath: @"C:\Users\Alice\example-service\service.exe",
                serviceId: "example-service.service",
                containerName: "example-container",
                scope: BindingScope.SpecificInterface,
                contexts: [InterfaceContext.LanPrivate]));

        ReportDocument report = ReportBuilder.CreateReport(generation, "1.0.0", ReportProfile.Full);
        ReportObservation observation = Assert.Single(report.Observations);

        Assert.Equal(ReportProfile.Full, report.Profile);
        Assert.Equal("192.168.1.50", observation.LocalAddress);
        Assert.Equal("203.0.113.9", observation.RemoteAddress);
        Assert.Equal("example-service", observation.ProcessName);
        Assert.Contains(@"C:\Users\Alice", observation.ExecutablePath, StringComparison.Ordinal);
        Assert.Equal("NotTested", observation.ExternalReachability);
        Assert.Equal("Available", observation.ProcessAvailability);
        Assert.Equal("Derived", observation.BindingEvidence);
        Assert.Equal("Available", observation.FirewallAvailability);
        Assert.Equal("firewall.no-rule", observation.FirewallExplanation);
        Assert.Equal(new DataReductionSummary(0, 0, 0), report.DataReductionSummary);
    }

    [Fact]
    public void ShareSafeReport_ReducesCommonIdentifiersAndPreservesBindingSemantics()
    {
        InventoryGeneration generation = TestData.Generation(
            TestData.Observation(
                id: "private",
                localAddress: "192.168.1.50",
                remoteAddress: "203.0.113.9",
                processName: "example-service",
                executablePath: @"C:\Users\Alice\example-service\service.exe",
                scope: BindingScope.SpecificInterface,
                contexts: [InterfaceContext.LanPrivate]));

        ReportDocument report = ReportBuilder.CreateReport(generation, "1.0.0", ReportProfile.ShareSafe);
        ReportObservation observation = Assert.Single(report.Observations);

        Assert.Equal(ReportProfile.ShareSafe, report.Profile);
        Assert.StartsWith("<PRIVATE-IP-", observation.LocalAddress, StringComparison.Ordinal);
        Assert.StartsWith("<PUBLIC-IP-", observation.RemoteAddress, StringComparison.Ordinal);
        Assert.DoesNotContain("Alice", observation.ExecutablePath, StringComparison.Ordinal);
        Assert.StartsWith("<PID-", observation.ProcessId, StringComparison.Ordinal);
        Assert.Equal("example-service", observation.ProcessName);
        Assert.True(report.DataReductionSummary.IpAddresses >= 2);
        Assert.True(report.DataReductionSummary.Paths >= 1);
        Assert.True(report.DataReductionSummary.ProcessIds >= 1);
    }

    [Fact]
    public void ShareSafeReport_UsesStableTokensInsideOneReport()
    {
        InventoryGeneration generation = TestData.Generation(
            TestData.Observation(id: "one", localAddress: "10.0.0.4"),
            TestData.Observation(id: "two", localAddress: "10.0.0.4", localPort: 9090));

        ReportDocument report = ReportBuilder.CreateReport(generation, "1.0.0", ReportProfile.ShareSafe);

        Assert.Equal(report.Observations[0].LocalAddress, report.Observations[1].LocalAddress);
    }

    [Fact]
    public void HtmlExport_EncodesUntrustedTextAndHasNoRemoteDependencies()
    {
        const string payload = "</td><script>alert('export')</script>";
        InventoryGeneration generation = TestData.Generation(TestData.Observation(processName: payload));
        ReportDocument report = ReportBuilder.CreateReport(generation, "1.0.0", ReportProfile.Full);

        string html = Encoding.UTF8.GetString(HtmlReportExporter.Export(report));

        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("default-src 'none'", html, StringComparison.Ordinal);
        Assert.Contains("External reachability was not tested", html, StringComparison.Ordinal);
        Assert.Contains("Full diagnostic report", html, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonExport_DeclaresTheSelectedProfile()
    {
        InventoryGeneration generation = TestData.Generation(
            TestData.Observation(localAddress: "172.16.1.8", executablePath: "/home/alice/bin/server"));
        ReportDocument report = ReportBuilder.CreateReport(generation, "1.0.0", ReportProfile.ShareSafe);

        byte[] json = JsonReportExporter.Export(report);
        using var document = JsonDocument.Parse(json);

        Assert.Equal(ReportBuilder.ReportSchemaIdentifier, document.RootElement.GetProperty("schema").GetString());
        Assert.Equal("shareSafe", document.RootElement.GetProperty("profile").GetString());
        string serialized = Encoding.UTF8.GetString(json);
        Assert.DoesNotContain("172.16.1.8", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("alice", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void ShareSafeReport_AppliesConfiguredAdditionalSensitiveFields()
    {
        InventoryGeneration generation = TestData.Generation(
            TestData.Observation(
                processName: "customer-agent",
                serviceId: "customer.service",
                containerName: "customer-container"));
        ReportRedactionOptions options = new(
            ReducePrivateIpAddresses: true,
            ReducePublicIpAddresses: true,
            ReduceUserNamesInPaths: true,
            ReduceProcessIds: true,
            ReduceProcessNames: true,
            ReduceServiceNames: true,
            ReduceContainerIdentifiers: true,
            ReduceFirewallRuleNames: true);

        ReportDocument report = ReportBuilder.CreateReport(
            generation,
            "1.0.0",
            ReportProfile.ShareSafe,
            options);
        ReportObservation observation = Assert.Single(report.Observations);

        Assert.StartsWith("<PROCESS-", observation.ProcessName, StringComparison.Ordinal);
        Assert.StartsWith("<SERVICE-", Assert.Single(observation.Services), StringComparison.Ordinal);
        Assert.Contains(":<CONTAINER-", observation.Container, StringComparison.Ordinal);
        Assert.Equal(1, report.DataReductionSummary.ProcessNames);
        Assert.Equal(1, report.DataReductionSummary.ServiceNames);
        Assert.Equal(1, report.DataReductionSummary.ContainerIdentifiers);
        Assert.Equal(1, report.DataReductionSummary.ObservationIds);
    }

    [Fact]
    public void ShareSafeReport_ReplacesCustomSensitiveLiteralsWithoutSerializingTheirPlaintext()
    {
        InventoryGeneration generation = TestData.Generation(
            TestData.Observation(
                processName: "Agent for Customer Alpha",
                executablePath: @"C:\Services\Customer Alpha\agent.exe",
                serviceId: "Customer Alpha.service",
                containerName: "Customer Alpha container"));
        ReportRedactionOptions options = ReportRedactionOptions.None;

        ReportDocument report = ReportBuilder.CreateReport(
            generation,
            "1.0.0",
            ReportProfile.ShareSafe,
            options,
            ["Customer Alpha"]);
        ReportObservation observation = Assert.Single(report.Observations);
        DataReductionSummary preview = ReportBuilder.PreviewReduction(
            generation,
            options,
            ["Customer Alpha"]);
        string json = Encoding.UTF8.GetString(JsonReportExporter.Export(report));

        Assert.DoesNotContain("Customer Alpha", observation.ProcessName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Customer Alpha", observation.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Customer Alpha", Assert.Single(observation.Services), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Customer Alpha", observation.Container, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<SENSITIVE-", observation.ProcessName, StringComparison.Ordinal);
        Assert.Equal(1, report.CustomSensitiveValueCount);
        Assert.Equal(4, report.DataReductionSummary.CustomSensitiveMatches);
        Assert.Equal(report.DataReductionSummary.CustomSensitiveMatches, preview.CustomSensitiveMatches);
        Assert.DoesNotContain("Customer Alpha", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("customSensitiveValues", json, StringComparison.Ordinal);
        Assert.Contains("\"customSensitiveValueCount\": 1", json, StringComparison.Ordinal);
    }

    [Fact]
    public void FullReport_IgnoresConfiguredCustomSensitiveValues()
    {
        InventoryGeneration generation = TestData.Generation(
            TestData.Observation(processName: "Customer Alpha agent"));

        ReportDocument report = ReportBuilder.CreateReport(
            generation,
            "1.0.0",
            ReportProfile.Full,
            new ReportRedactionOptions(),
            ["Customer Alpha"]);

        Assert.Contains("Customer Alpha", Assert.Single(report.Observations).ProcessName, StringComparison.Ordinal);
        Assert.Equal(0, report.CustomSensitiveValueCount);
        Assert.Equal(0, report.DataReductionSummary.CustomSensitiveMatches);
    }

    [Fact]
    public void SensitiveValuePolicy_NormalizesDuplicatesAndRejectsUnsafeBounds()
    {
        ImmutableArray<string> normalized = SensitiveValuePolicy.Normalize(
            [" Customer Alpha ", "customer alpha", "Ticket-42"]);

        Assert.Equal(["Customer Alpha", "Ticket-42"], normalized);
        Assert.Throws<ArgumentException>(() => SensitiveValuePolicy.Normalize(["ab"]));
        Assert.Throws<ArgumentException>(() => SensitiveValuePolicy.Normalize(["valid\u0001value"]));
        Assert.Throws<ArgumentException>(() => SensitiveValuePolicy.Normalize(
            Enumerable.Range(0, SensitiveValuePolicy.MaximumValueCount + 1)
                .Select(index => $"value-{index}")));
    }

    [Fact]
    public void Report_ExportsFirewallCandidateDetailsAndReducesRuleNames()
    {
        SocketObservation source = TestData.Observation(localAddress: "192.168.10.5");
        FirewallRuleEvidence rule = new(
            "customer-rule-id",
            "Customer inbound service",
            FirewallRuleAction.Allow,
            FirewallRuleDirection.Inbound,
            "6",
            "8080",
            "192.168.10.0/24",
            "private",
            "test.match",
            []);
        InventoryGeneration generation = TestData.Generation(source with
        {
            Firewall = new FirewallEvaluation(
                FirewallAssessment.AllowRuleMayMatch,
                [rule],
                DataAvailability.Available,
                "test.allow-candidate"),
        });

        ReportDocument report = ReportBuilder.CreateReport(generation, "1.0.0", ReportProfile.ShareSafe);
        ReportFirewallRule exported = Assert.Single(Assert.Single(report.Observations).FirewallCandidates);

        Assert.StartsWith("<FIREWALL-RULE-", exported.Name, StringComparison.Ordinal);
        Assert.StartsWith("<PRIVATE-IP-", exported.LocalAddresses, StringComparison.Ordinal);
        Assert.Equal("8080", exported.LocalPorts);
        Assert.Equal(1, report.DataReductionSummary.FirewallRuleNames);
        Assert.True(report.DataReductionSummary.IpAddresses >= 2);
    }

    [Fact]
    public void PreviewReduction_HandlesMaximumSupportedObservationCountWithoutBuildingAReport()
    {
        ImmutableArray<SocketObservation>.Builder observations =
            ImmutableArray.CreateBuilder<SocketObservation>(100_000);
        for (int index = 0; index < 100_000; index++)
        {
            observations.Add(TestData.Observation(
                id: $"large-{index}",
                localAddress: "10.0.0.5",
                localPort: index % 65_536));
        }

        InventoryGeneration generation = TestData.Generation() with
        {
            Observations = observations.MoveToImmutable(),
        };

        DataReductionSummary preview = ReportBuilder.PreviewReduction(
            generation,
            new ReportRedactionOptions());

        Assert.Equal(100_000, preview.ObservationIds);
        Assert.Equal(100_000, preview.IpAddresses);
        Assert.Equal(100_000, preview.ProcessIds);
    }
}

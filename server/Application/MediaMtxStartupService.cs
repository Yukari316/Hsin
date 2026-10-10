using Hsin.Media.Configuration;
using Hsin.Media.Installation;
using SerilogLogger = Serilog.ILogger;

namespace Hsin.Application;

internal sealed class MediaMtxStartupService
{
    private readonly MediaMtxInstallationService installation;
    private readonly MediaMtxProcessService      process;
    private readonly ResolvedConfiguration       configuration;
    private readonly SerilogLogger               logger;

    public MediaMtxStartupService(
        MediaMtxInstallationService installation,
        MediaMtxProcessService      process,
        ResolvedConfiguration       configuration,
        SerilogLogger               logger)
    {
        this.installation  = installation;
        this.process       = process;
        this.configuration = configuration;
        this.logger        = logger.ForContext("Component", "MediaMTX.Startup");
    }

    public async Task<InstalledMediaVersion> StartAsync(
        string?           mediaPackage,
        CancellationToken cancellationToken)
    {
        InstallationState state = mediaPackage is null
            ? await installation.EnsureAsync(cancellationToken)
            : await installation.ImportAsync(mediaPackage, cancellationToken);
        string generatedConfiguration = Path.Combine(configuration.DataDirectory, "run", "mediamtx.yml");
        await MediaMtxConfiguration.WriteAsync(configuration, generatedConfiguration, cancellationToken);
        InstalledMediaVersion selected = state.Pending
                                         ?? state.Active
                                         ?? throw new InvalidDataException("There is no installed MediaMTX version.");
        try
        {
            await StartCandidateAsync(selected, generatedConfiguration, cancellationToken);
            await installation.CommitReadyAsync(selected, cancellationToken);
            return selected;
        }
        catch (Exception candidateError) when (
            state.Pending is not null && state.Active is not null && mediaPackage is null)
        {
            logger.Error(
                candidateError,
                "MediaMTX candidate {Version} failed; restoring {PreviousVersion}",
                selected.Id,
                state.Active.Id);
            await process.StopAsync(CancellationToken.None);
            await installation.ClearPendingAsync(cancellationToken);
            await StartCandidateAsync(state.Active, generatedConfiguration, cancellationToken);
            return state.Active;
        }
        catch when (mediaPackage is not null && state.Pending is not null)
        {
            await process.StopAsync(CancellationToken.None);
            try
            {
                await installation.ClearFailedImportAsync(state, cancellationToken);
            }
            catch (Exception cleanupError)
            {
                logger.Warning(
                    cleanupError,
                    "Failed to clean rejected MediaMTX package {InstallationId}",
                    state.Pending.Id);
            }

            throw;
        }
        catch
        {
            await process.StopAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task StartCandidateAsync(
        InstalledMediaVersion candidate,
        string                generatedConfiguration,
        CancellationToken     cancellationToken)
    {
        await installation.VerifyInstalledBinaryAsync(candidate, cancellationToken);
        string executable = installation.GetExecutablePath(candidate.Id);
        await process.StartAsync(executable, generatedConfiguration, cancellationToken);
    }
}

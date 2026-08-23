using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RetroShare.Application.DTOs;
using RetroShare.Application.Interfaces;
using RetroShare.Domain.Constants;

namespace RetroShare.API.Controllers;

/// <summary>Live server resource monitoring. Permission-gated (system.monitor) and read-only:
/// returns only the derived monitoring DTO, never paths, environment or credentials.</summary>
[ApiController]
[Route("api/system")]
public sealed class SystemMonitorController(ISystemMonitorService monitor) : ControllerBase
{
    /// <summary>Current CPU/RAM/disk usage, uptime and RetroShare data footprint.</summary>
    [HttpGet("monitor")]
    [Authorize(Policy = Permissions.SystemMonitor)]
    [ProducesResponseType(typeof(SystemMonitorDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Monitor(CancellationToken ct) => Ok(await monitor.GetSnapshotAsync(ct));
}

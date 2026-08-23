using Microsoft.Extensions.DependencyInjection;
using RetroShare.Application.Interfaces;
using RetroShare.Application.Services;

namespace RetroShare.Application;

public static class DependencyInjection
{
    /// <summary>Registers the Application services. Repository, storage and security
    /// implementations are supplied by the Infrastructure layer.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<IFileService, FileService>();
        services.AddScoped<IFolderService, FolderService>();
        services.AddScoped<IShareService, ShareService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<ISystemMonitorService, SystemMonitorService>();
        services.AddScoped<IActivityService, ActivityService>();
        services.AddScoped<IActivityLogger, ActivityLogger>();
        return services;
    }
}

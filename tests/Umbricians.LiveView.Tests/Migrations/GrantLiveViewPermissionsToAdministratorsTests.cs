using System.Reflection;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbricians.LiveView.Migrations;

namespace Umbricians.LiveView.Tests.Migrations;

/// <summary>The install step that gives the Administrators group every Live View permission.</summary>
public class GrantLiveViewPermissionsToAdministratorsTests
{
    [Fact]
    public async Task Grants_every_permission_to_the_group_with_the_administrators_key()
    {
        var groups = new UserGroups(new UserGroupProxy.Group(Umbraco.Cms.Core.Constants.Security.AdminGroupKey, "renamed-admin"));

        await Run(groups);

        Assert.Equal(Constants.Permissions.All.Order(), groups.Administrators!.Permissions.Order());
        Assert.Equal(1, groups.Updates);
    }

    [Fact]
    public async Task Leaves_the_group_alone_when_it_already_has_every_permission()
    {
        var administrators = new UserGroupProxy.Group(Umbraco.Cms.Core.Constants.Security.AdminGroupKey, "admin");
        administrators.Permissions.UnionWith(Constants.Permissions.All);
        var groups = new UserGroups(administrators);

        await Run(groups);

        Assert.Equal(0, groups.Updates);
    }

    [Fact]
    public async Task Does_nothing_when_there_is_no_administrators_group()
    {
        var groups = new UserGroups(null);

        await Run(groups);

        Assert.Equal(0, groups.Updates);
    }

    private static Task Run(UserGroups groups) =>
        new Step(StandIn.Create<IMigrationContext>(_ => null), StandIn.Create<IUserGroupService>(groups.Handle)).RunAsync();

    /// <summary>Calls the step's migration directly; Umbraco's runner needs a database.</summary>
    private sealed class Step(IMigrationContext context, IUserGroupService userGroups)
        : GrantLiveViewPermissionsToAdministrators(context, userGroups)
    {
        public Task RunAsync() => MigrateAsync();
    }

    /// <summary>A user group service that finds the Administrators group only by its key.</summary>
    private sealed class UserGroups(UserGroupProxy.Group? administrators)
    {
        public UserGroupProxy.Group? Administrators { get; } = administrators;

        public int Updates { get; private set; }

        public object? Handle(StandIn.Call call)
        {
            switch (call.Method.Name)
            {
                case nameof(IUserGroupService.GetAsync) when call.Args is [Guid key]:
                    return Task.FromResult<IUserGroup?>(key == Administrators?.Key ? Administrators.AsUserGroup() : null);
                case nameof(IUserGroupService.UpdateAsync):
                    Updates++;
                    return call.CompletedWithDefault();
                default:
                    throw new NotSupportedException($"{call.Method.Name}({string.Join(", ", call.Args.Select(a => a?.GetType().Name))}) isn't expected.");
            }
        }
    }

    /// <summary>An <see cref="IUserGroup"/> with a key, an alias and a permission set; anything else throws.</summary>
    public class UserGroupProxy : DispatchProxy
    {
        private Group _group = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            "get_Key" => _group.Key,
            "get_Alias" => _group.Alias,
            "get_Permissions" => _group.Permissions,
            _ => throw new NotSupportedException(targetMethod?.Name),
        };

        public sealed record Group(Guid Key, string Alias)
        {
            public ISet<string> Permissions { get; } = new HashSet<string>();

            public IUserGroup AsUserGroup()
            {
                IUserGroup group = Create<IUserGroup, UserGroupProxy>();
                ((UserGroupProxy)(object)group)._group = this;
                return group;
            }
        }
    }
}

/// <summary>An implementation of any interface whose calls go to a handler, for Umbraco services too big to fake by hand.</summary>
public class StandIn : DispatchProxy
{
    private Func<Call, object?> _handler = null!;

    public static T Create<T>(Func<Call, object?> handler) where T : class
    {
        T proxy = Create<T, StandIn>();
        ((StandIn)(object)proxy)._handler = handler;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        _handler(new Call(targetMethod!, args ?? []));

    public sealed record Call(MethodInfo Method, object?[] Args)
    {
        /// <summary>A completed task of the method's return type, holding the default result.</summary>
        public object CompletedWithDefault()
        {
            Type result = Method.ReturnType.GetGenericArguments()[0];
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(result)
                .Invoke(null, [result.IsValueType ? Activator.CreateInstance(result) : null])!;
        }
    }
}

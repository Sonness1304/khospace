using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Identity;

namespace WarehouseRent.Tests;

public sealed class PostgresCommandTransactionTests
{
    [Fact]
    public async Task Actual_serializable_conflict_rolls_back_then_same_key_can_commit_and_replay_without_second_write()
    {
        await using var fixture=await PostgresRuntimeFixture.Create();
        var id=Guid.NewGuid();var rolledBackId=Guid.NewGuid();var scope=Guid.NewGuid();var key=Guid.NewGuid();
        await using(var seed=fixture.Context()) await seed.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO khospace.\"Facilities\" (\"Id\",\"Code\",\"Name\",\"Address\",\"PermittedGoods\",\"TimeZoneId\",\"Published\") VALUES ({id},'BEFORE','Before','Fixture','General','Asia/Ho_Chi_Minh',false)");
        DefaultHttpContext Request(){var c=new DefaultHttpContext();c.Request.Headers["Idempotency-Key"]=key.ToString();c.User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,"synthetic-actor")},"fixture"));return c;}
        var writes=0;var callbacks=0;
        await using(var first=fixture.Context()) {
            var request=Request();var executor=new CommandExecutor(first,new CurrentActor(first,new HttpContextAccessor{HttpContext=request}));
            var result=await executor.ExecuteAsync(request,scope,"SyntheticSerializable",new {value=1},async ct=>{
                writes++;
                _=await first.Facilities.AsNoTracking().SingleAsync(x=>x.Id==id,ct);
                await first.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO khospace.\"Facilities\" (\"Id\",\"Code\",\"Name\",\"Address\",\"PermittedGoods\",\"TimeZoneId\",\"Published\") VALUES ({rolledBackId},'ROLLBACK','Rollback','Fixture','General','Asia/Ho_Chi_Minh',false)",ct);
                await using(var other=fixture.Context()) await other.Database.ExecuteSqlInterpolatedAsync($"UPDATE khospace.\"Facilities\" SET \"Name\"='Other committed' WHERE \"Id\"={id}",ct);
                // This real concurrent update raises PostgreSQL40001 against the frozen Serializable snapshot.
                await first.Database.ExecuteSqlInterpolatedAsync($"UPDATE khospace.\"Facilities\" SET \"Name\"='Must not commit' WHERE \"Id\"={id}",ct);
                return new {value=1};
            },CancellationToken.None,serializableAfterReceipt:true,onCommitted:()=>callbacks++);
            Assert.Equal(409,Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
            Assert.Contains("COMMAND_RETRY_REQUIRED",System.Text.Json.JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(result).Value));
        }
        Assert.Equal(1,writes);Assert.Equal(0,callbacks);
        await using(var check=fixture.Context()) {
            Assert.False(await check.Facilities.AnyAsync(x=>x.Id==rolledBackId));Assert.Empty(await check.OperationalCommands.ToListAsync());
            Assert.Equal("Other committed",await check.Facilities.Where(x=>x.Id==id).Select(x=>x.Name).SingleAsync());
        }
        string response;
        await using(var retry=fixture.Context()) {
            var request=Request();var executor=new CommandExecutor(retry,new CurrentActor(retry,new HttpContextAccessor{HttpContext=request}));
            var result=await executor.ExecuteAsync(request,scope,"SyntheticSerializable",new {value=1},ct=>{writes++;return Task.FromResult<object>(new {value=1});},CancellationToken.None,serializableAfterReceipt:true,onCommitted:()=>callbacks++);
            response=Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.HttpResults.ContentHttpResult>(result).ResponseContent!;
            Assert.Equal(1,await retry.OperationalCommands.CountAsync());
        }
        await using(var replay=fixture.Context()) {
            var request=Request();var executor=new CommandExecutor(replay,new CurrentActor(replay,new HttpContextAccessor{HttpContext=request}));
            var result=await executor.ExecuteAsync(request,scope,"SyntheticSerializable",new {value=1},ct=>throw new InvalidOperationException("Replay must not call write"),CancellationToken.None,serializableAfterReceipt:true,onCommitted:()=>callbacks++);
            Assert.Equal(response,Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.HttpResults.ContentHttpResult>(result).ResponseContent);
        }
        Assert.Equal(2,writes);Assert.Equal(1,callbacks);
    }
}


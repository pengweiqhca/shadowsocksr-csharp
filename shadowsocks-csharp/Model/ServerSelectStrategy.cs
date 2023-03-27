﻿namespace Shadowsocks.Model;

public class ServerSelectStrategy
{
    public delegate bool FilterFunc(Server server, Server selServer); // return true if select the server
    private Random randomGennarator;
    private int lastSelectIndex;
    private string lastSelectID;
    private DateTime lastSelectTime;
    private int lastUserSelectIndex;
    private const int MAX_CHANCE = 10000;
    private const int ERROR_PENALTY = MAX_CHANCE / 20;
    private const int CONNECTION_PENALTY = MAX_CHANCE / 100;
    private const int MIN_CHANCE = 10;

    private readonly struct ServerIndex
    {
        public readonly int index;
        public readonly Server server;
        public ServerIndex(int i, Server s)
        {
            index = i;
            server = s;
        }
    };
    private int lowerBound(List<double> data, double target)
    {
        var left = 0;
        var right = data.Count - 1;
        while (left < right)
        {
            var mid = (left + right) / 2;
            if (data[mid] >= target)
                right = mid;
            else if (data[mid] < target)
                left = mid + 1;
        }
        return left;
    }

    private double Algorithm2(ServerSpeedLog serverSpeedLog) // perfer less delay
    {
        if (serverSpeedLog.ErrorContinurousTimes >= 20)
            return 1;
        if (serverSpeedLog.ErrorContinurousTimes >= 10)
            return MIN_CHANCE;
        if (serverSpeedLog is { AvgConnectTime: < 0, TotalConnectTimes: >= 3 })
            return MIN_CHANCE;
        if (serverSpeedLog.TotalConnectTimes < 1)
            return MAX_CHANCE;
        var avgConnectTime = serverSpeedLog.AvgConnectTime <= 0 ? 1 : serverSpeedLog.AvgConnectTime;
        if (serverSpeedLog is { TotalConnectTimes: >= 1, AvgConnectTime: < 0 })
            avgConnectTime = 5000;
        var connections = serverSpeedLog.TotalConnectTimes - serverSpeedLog.TotalDisconnectTimes;
        var chance = MAX_CHANCE * 10.0 / avgConnectTime - connections * CONNECTION_PENALTY;
        if (chance > MAX_CHANCE) chance = MAX_CHANCE;
        chance -= serverSpeedLog.ErrorContinurousTimes * ERROR_PENALTY;
        if (chance < MIN_CHANCE) chance = MIN_CHANCE;
        return chance;
    }

    private double Algorithm3(ServerSpeedLog serverSpeedLog) // perfer less error
    {
        if (serverSpeedLog.ErrorContinurousTimes >= 20)
            return 1;
        if (serverSpeedLog.ErrorContinurousTimes >= 10)
            return MIN_CHANCE;
        if (serverSpeedLog is { AvgConnectTime: < 0, TotalConnectTimes: >= 3 })
            return MIN_CHANCE;
        if (serverSpeedLog.TotalConnectTimes < 1)
            return MAX_CHANCE;
        var avgConnectTime = serverSpeedLog.AvgConnectTime <= 0 ? 1 : serverSpeedLog.AvgConnectTime / 1000 * 1000;
        if (serverSpeedLog is { TotalConnectTimes: >= 1, AvgConnectTime: < 0 })
            avgConnectTime = 5000;
        var connections = serverSpeedLog.TotalConnectTimes - serverSpeedLog.TotalDisconnectTimes;
        var chance = MAX_CHANCE * 1.0 / (avgConnectTime / 500 + 1) - connections * CONNECTION_PENALTY;
        if (chance > MAX_CHANCE) chance = MAX_CHANCE;
        chance -= serverSpeedLog.ErrorContinurousTimes * ERROR_PENALTY;
        if (chance < MIN_CHANCE) chance = MIN_CHANCE;
        return chance;
    }

    private double Algorithm4(ServerSpeedLog serverSpeedLog, long avg_speed, double zero_chance) // perfer fast speed
    {
        if (serverSpeedLog.ErrorContinurousTimes >= 20)
            return 1;
        if (serverSpeedLog.ErrorContinurousTimes >= 10)
            return MIN_CHANCE;
        if (serverSpeedLog is { AvgConnectTime: < 0, TotalConnectTimes: >= 3 })
            return MIN_CHANCE;
        if (serverSpeedLog.TotalConnectTimes < 1)
            return MAX_CHANCE;
        var avgConnectTime = serverSpeedLog.AvgConnectTime <= 0 ? 1 : serverSpeedLog.AvgConnectTime / 2000 * 2000;
        serverSpeedLog.GetTransSpeed(out var speed_u, out var speed_d);
        if (serverSpeedLog is { TotalConnectTimes: >= 1, AvgConnectTime: < 0 })
            avgConnectTime = 5000;
        var speed_mul = speed_d > avg_speed ? 1.0 :
            speed_d == 0 ? zero_chance :
            speed_d < avg_speed / 2 ? 0.001 : 0.005;
        var connections = serverSpeedLog.TotalConnectTimes - serverSpeedLog.TotalDisconnectTimes;
        var chance = MAX_CHANCE * speed_mul / (avgConnectTime / 500 + 1) - connections * CONNECTION_PENALTY;
        if (chance > MAX_CHANCE) chance = MAX_CHANCE;
        chance -= serverSpeedLog.ErrorContinurousTimes * ERROR_PENALTY;
        if (chance < MIN_CHANCE) chance = MIN_CHANCE;
        return chance;
    }

    protected int SubSelect(List<Server> configs, int curIndex, string algorithm, FilterFunc filter, bool forceChange)
    {
        if (randomGennarator == null)
        {
            randomGennarator = new Random();
            lastSelectIndex = -1;
        }
        if (configs.Count <= lastSelectIndex || lastSelectIndex < 0)
        {
            lastSelectIndex = -1;
            lastSelectTime = DateTime.Now;
            lastUserSelectIndex = -1;
        }
        else
        {
            if (configs[lastSelectIndex].id != lastSelectID)
            {
                if (lastSelectID != null)
                {
                    for (var i = 0; i < configs.Count; ++i)
                    {
                        if (configs[i].id == lastSelectID)
                        {
                            lastSelectIndex = i;
                            break;
                        }
                    }
                }
                if (configs[lastSelectIndex].id != lastSelectID)
                {
                    lastSelectIndex = -1;
                    lastSelectTime = DateTime.Now;
                    lastUserSelectIndex = -1;
                }
            }
        }
        if (lastUserSelectIndex != curIndex)
        {
            if (configs.Count > curIndex && curIndex >= 0 && algorithm != "Timer")
            {
                lastSelectIndex = curIndex;
            }
            lastUserSelectIndex = curIndex;
        }
        if (lastSelectIndex == -1)
        {
            if (configs.Count > curIndex && curIndex >= 0)
            {
                lastSelectIndex = curIndex;
            }
        }
        if (configs.Count > 0)
        {
            var serverList = new List<ServerIndex>();
            for (var i = 0; i < configs.Count; ++i)
            {
                if (configs[i].isEnable())
                {
                    if (filter != null)
                    {
                        if (!filter(configs[i], lastSelectIndex < 0 ? null : configs[lastSelectIndex]))
                            continue;
                    }
                    serverList.Add(new ServerIndex(i, configs[i]));
                }
            }
            if (serverList.Count == 0 && filter != null)
            {
                for (var i = 0; i < configs.Count; ++i)
                {
                    if (!filter(configs[i], lastSelectIndex < 0 ? null : configs[lastSelectIndex]))
                        continue;
                    serverList.Add(new ServerIndex(i, configs[i]));
                }
            }
            if (forceChange && serverList.Count > 1 && algorithm != "OneByOne")
            {
                for (var i = 0; i < serverList.Count; ++i)
                {
                    if (serverList[i].index == lastSelectIndex)
                    {
                        serverList.RemoveAt(i);
                        break;
                    }
                }
            }
            if (serverList.Count == 0)
            {
                var i = lastSelectIndex;
                if (i >= 0 && i < configs.Count && configs[i].isEnable())
                    serverList.Add(new ServerIndex(i, configs[i]));
            }
            var serverListIndex = -1;
            if (serverList.Count > 0)
            {
                if (algorithm == "OneByOne")
                {
                    var selIndex = -1;
                    for (var i = 0; i < serverList.Count; ++i)
                    {
                        if (serverList[i].index == lastSelectIndex)
                        {
                            selIndex = i;
                            break;
                        }
                    }
                    serverListIndex = serverList[(selIndex + 1) % serverList.Count].index;
                }
                else if (algorithm == "Random")
                {
                    serverListIndex = randomGennarator.Next(serverList.Count);
                    serverListIndex = serverList[serverListIndex].index;
                }
                else if (algorithm is "LowException" or "Timer" or "FastDownloadSpeed")
                {
                    if (algorithm == "Timer")
                    {
                        if ((DateTime.Now - lastSelectTime).TotalSeconds > 60 * 5)
                        {
                            lastSelectTime = DateTime.Now;
                        }
                        else
                        {
                            if (configs.Count > lastSelectIndex && lastSelectIndex >= 0 && configs[lastSelectIndex].isEnable() && !forceChange)
                            {
                                return lastSelectIndex;
                            }
                        }
                    }
                    var chances = new List<double>();
                    double lastBeginVal = 0;
                    if (algorithm == "FastDownloadSpeed")
                    {
                        long avg_speed = 1024 * 64;
                        long sum_speed = 0;
                        var sum_cnt = 0;
                        var zero_cnt = 0;
                        foreach (var s in serverList)
                        {
                            s.server.ServerSpeedLog().GetTransSpeed(out var speed_u, out var speed_d);
                            if (speed_d == 0)
                                ++zero_cnt;
                            else
                            {
                                sum_speed += speed_d;
                                ++sum_cnt;
                            }
                        }
                        var zero_chance = 0.5;
                        if (sum_cnt > 0)
                        {
                            avg_speed = sum_speed / sum_cnt;
                            if (zero_cnt + sum_cnt > 0)
                                zero_chance = 0.1 * sum_cnt / (zero_cnt + sum_cnt);
                        }
                        foreach (var s in serverList)
                        {
                            var chance = Algorithm4(s.server.ServerSpeedLog(), avg_speed, zero_chance);
                            if (chance > 0)
                            {
                                chances.Add(lastBeginVal + chance);
                                lastBeginVal += chance;
                            }
                        }
                    }
                    else
                    {
                        foreach (var s in serverList)
                        {
                            var chance = Algorithm3(s.server.ServerSpeedLog());
                            if (chance > 0)
                            {
                                chances.Add(lastBeginVal + chance);
                                lastBeginVal += chance;
                            }
                        }
                    }
                    {
                        var target = randomGennarator.NextDouble() * lastBeginVal;
                        serverListIndex = lowerBound(chances, target);
                        serverListIndex = serverList[serverListIndex].index;
                        return serverListIndex;
                    }
                }
                else //if (algorithm == (int)SelectAlgorithm.LowLatency || algorithm == (int)SelectAlgorithm.SelectedFirst)
                {
                    var chances = new List<double>();
                    double lastBeginVal = 0;
                    foreach (var s in serverList)
                    {
                        var chance = Algorithm2(s.server.ServerSpeedLog());
                        if (chance > 0)
                        {
                            chances.Add(lastBeginVal + chance);
                            lastBeginVal += chance;
                        }
                    }
                    if (algorithm == "SelectedFirst"
                        && randomGennarator.Next(3) == 0
                        && configs[curIndex].isEnable())
                    {
                        for (var i = 0; i < serverList.Count; ++i)
                        {
                            if (curIndex == serverList[i].index)
                            {
                                return curIndex;
                            }
                        }
                    }
                    {
                        var target = randomGennarator.NextDouble() * lastBeginVal;
                        serverListIndex = lowerBound(chances, target);
                        serverListIndex = serverList[serverListIndex].index;
                        return serverListIndex;
                    }
                }
            }
            return serverListIndex;
        }
        return -1;
    }

    public int Select(List<Server> configs, int curIndex, string algorithm, FilterFunc filter, bool forceChange = false)
    {
        lastSelectIndex = SubSelect(configs, curIndex, algorithm, filter, forceChange);
        if (lastSelectIndex >= 0 && lastSelectIndex < configs.Count)
        {
            lastSelectID = configs[lastSelectIndex].id;
        }
        else
        {
            lastSelectID = null;
        }
        return lastSelectIndex;
    }
}
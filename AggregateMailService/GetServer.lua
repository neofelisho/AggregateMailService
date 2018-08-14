local function GetServer(keys)
    for _,key in ipairs(keys) do
        local count = redis.call('GET', key)
        if(count ~= 1) then
            local current = redis.call('INCR', key)
            if(current == 1) then
                redis.call('EXPIRE', key, 60)
                return key
            end
        end
    end
    return nil
end
return GetServer(KEYS)
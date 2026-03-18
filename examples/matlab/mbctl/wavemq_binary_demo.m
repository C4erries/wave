% wave-mq demo wrapper built on mbctl.
% Build mbctl first with:
% powershell -ExecutionPolicy Bypass -File .\examples\build-mbctl.ps1

brokerAddr = "127.0.0.1:7912";
topic = sprintf("demo.binary.%d", floor(posixtime(datetime("now", "TimeZone", "UTC"))));
group = sprintf("demo-group-%d", floor(posixtime(datetime("now", "TimeZone", "UTC"))));
partition = int32(0);
values = [
    "hello from matlab"
    "second record"
    "third record"
];

scriptPath = mfilename("fullpath");
scriptDir = fileparts(scriptPath);
repoRoot = fileparts(fileparts(fileparts(scriptDir)));
mbctlPath = fullfile(repoRoot, "examples", "bin", "mbctl.exe");

if ~isfile(mbctlPath)
    error("wave:mbctl", "mbctl not found at %s. Run examples/build-mbctl.ps1 first.", mbctlPath);
end

fprintf("broker=%s\n", brokerAddr);
fprintf("mbctl=%s\n", mbctlPath);

fprintf("1. ping\n");
runMbctl(mbctlPath, brokerAddr, ["ping"]);
fprintf("pong\n");

fprintf("2. create topic %s\n", topic);
createResult = runMbctl(mbctlPath, brokerAddr, [
    "create-topic", ...
    "-topic", topic, ...
    "-partitions", "1", ...
    "-replication-factor", "1" ...
]);
fprintf("topic created partitions=%d rf=%d\n", createResult.partitions, createResult.replicationFactor);

fprintf("3. metadata\n");
metadataResult = runMbctl(mbctlPath, brokerAddr, ["metadata", "-topic", topic]);
printMetadata(metadataResult);

fprintf("4. produce\n");
baseOffset = [];
for idx = 1:numel(values)
    produceResult = runMbctl(mbctlPath, brokerAddr, [
        "produce", ...
        "-topic", topic, ...
        "-partition", string(partition), ...
        "-value", values(idx) ...
    ]);
    if isempty(baseOffset)
        baseOffset = produceResult.baseOffset;
    end
end
fprintf("base_offset=%d\n", baseOffset);

fprintf("5. list offsets\n");
offsetsResult = runMbctl(mbctlPath, brokerAddr, [
    "list-offsets", ...
    "-topic", topic, ...
    "-partition", string(partition) ...
]);
fprintf("earliest=%d latest=%d\n", offsetsResult.earliest, offsetsResult.latest);

fprintf("6. fetch\n");
fetchResult = runMbctl(mbctlPath, brokerAddr, [
    "fetch", ...
    "-topic", topic, ...
    "-partition", string(partition), ...
    "-offset", "0", ...
    "-max-bytes", "1048576" ...
]);
printFetch(fetchResult);

if isfield(fetchResult, "records") && ~isempty(fetchResult.records)
    records = fetchResult.records;
    committedOffset = records(end).offset;

    fprintf("7. commit offset group=%s offset=%d\n", group, committedOffset);
    runMbctl(mbctlPath, brokerAddr, [
        "commit-offset", ...
        "-group", group, ...
        "-topic", topic, ...
        "-partition", string(partition), ...
        "-offset", string(committedOffset) ...
    ]);
    fprintf("commit ok\n");

    fprintf("8. fetch committed\n");
    committedResult = runMbctl(mbctlPath, brokerAddr, [
        "fetch-committed", ...
        "-group", group, ...
        "-topic", topic, ...
        "-partition", string(partition) ...
    ]);
    fprintf("committed offset=%d\n", committedResult.offset);
end


function result = runMbctl(mbctlPath, brokerAddr, args)
cmdArgs = [ ...
    string(mbctlPath)
    args(:).'
    "-broker", brokerAddr
    "-json"
];

quoted = strings(1, numel(cmdArgs));
for idx = 1:numel(cmdArgs)
    quoted(idx) = quoteArg(cmdArgs(idx));
end

command = strjoin(quoted, " ");
[status, output] = system(char(command));
if status ~= 0
    error("wave:mbctl", "mbctl failed: %s", strtrim(output));
end

payload = strtrim(output);
if strlength(string(payload)) == 0
    error("wave:mbctl", "mbctl returned empty stdout");
end

result = jsondecode(payload);
if ~isfield(result, "ok") || ~result.ok
    error("wave:mbctl", "unexpected mbctl response");
end
end


function printMetadata(result)
if ~isfield(result, "partitions") || isempty(result.partitions)
    fprintf("metadata: no partitions returned\n");
    return
end

for idx = 1:numel(result.partitions)
    item = result.partitions(idx);
    fprintf( ...
        "topic=%s partition=%d broker=%d role=%s leader=%d start=%d hwm=%d replicas=%s isr=%s\n", ...
        item.topic, ...
        item.partition, ...
        item.brokerId, ...
        item.role, ...
        item.leader, ...
        item.startOffset, ...
        item.highWatermark, ...
        mat2str(item.replicas), ...
        mat2str(item.isr));
end
end


function printFetch(result)
if ~isfield(result, "records")
    fprintf("high_watermark=%d records=0\n", result.highWatermark);
    return
end

records = result.records;
fprintf("high_watermark=%d records=%d\n", result.highWatermark, numel(records));
for idx = 1:numel(records)
    record = records(idx);
    fprintf("offset=%d key=%s value=%s\n", record.offset, record.key, record.value);
end
end


function value = quoteArg(arg)
value = '"' + string(arg) + '"';
end

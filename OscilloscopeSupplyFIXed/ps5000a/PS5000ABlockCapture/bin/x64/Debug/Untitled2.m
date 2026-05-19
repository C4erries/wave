FF = load("TransDmitryiAbs.txt");
FF(:,2)=FF(:,2)/max(FF(:,2));
figure;
plot(FF(:,1)*1d-3,FF(:,2)); grid;

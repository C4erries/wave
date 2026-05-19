clear; clc;
file_name='nArray.txt';
f1=1d0; f2=150d0; df=0.2d0; 
t1=100; t2=10000; 
AVG_B=50;
y2=load(file_name); y2=y2-sum(y2(1:AVG_B))/AVG_B;
NN_time=numel(y2); 
dt=104e-9;
max_f=1d0/dt;
x2=0:dt:(NN_time-1)*dt;
figure; plot(x2*1d3,y2); grid;
figure; plot(y2); grid; hold; 
%break;

f_val(1,1:NN_time)=y2*1d3;
t_var=x2*1d3;
dt=t_var(1,2)-t_var(1,1);
N_tr_1=t1; N_tr_2=t2; %NN_time/4;
NN_f=(f2-f2)/df+1; sp(1,1:NN_f)=0d0; ff(1,1:NN_f)=0d0; sp_all(1:NN_f)=0+0i;
t_i=0;
for t_f=f1:df:f2
    t_omega=t_f*2d0*pi;
    t_sum=0+0i;
    wt=t_omega*dt;
    t_sp_val=0+0i;
    for i1=N_tr_1:N_tr_2
        t_sp_val=2d0*exp(1i*t_omega*t_var(1,i1))*dt*(1-cos(wt))/(wt*wt);
        t_sum=t_sum+f_val(1,i1)*t_sp_val;
    end
    t_i=t_i+1;
    sp(1,t_i)=abs(t_sum); sp_all(t_i)=t_sum;
    ff(1,t_i)=t_f;
end
%
sp_all=sp_all*1d1;
figure; plot(ff(1,:),sp(1,:)/max(sp(1,:))); grid;
%figure(23); plot(ff,real(sp_all),ff,imag(sp_all)); grid;
y=[ff(1,:); sp(1,:)];
fid = fopen('spectrum.dat','wt');
fprintf(fid,'%12.6f  %12.6f\n',y);
fclose(fid); clear y;
%
y=[real(sp_all(1,:)); imag(sp_all(1,:))];
fid = fopen('TRV_spectrum_exp.dat','wt');
fprintf(fid,'%10.7f %10.7f\n',y);
fclose(fid); clear y;